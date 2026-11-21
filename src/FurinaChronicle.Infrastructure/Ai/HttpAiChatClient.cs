// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Infrastructure.Ai;

public sealed class HttpAiChatClient : IAiChatClient, IDisposable
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    public HttpAiChatClient(HttpClient? client = null)
    {
        ownsClient = client is null;
        this.client = client ?? new HttpClient(new HttpClientHandler
        { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        if (this.client.DefaultRequestHeaders.Any(h => h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
            h.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase) || h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)))
            throw new AiAnalysisException("模型客户端不允许默认认证或 Cookie；请使用供应商独立密钥。");
    }
    public async Task<IReadOnlyList<string>> ListModelsAsync(AiServiceSettings settings, string? apiKey, CancellationToken cancellationToken = default)
    {
        settings.Validate(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var result = new List<string>(); var cursors = new HashSet<string>(StringComparer.Ordinal); string? cursor = null;
        try
        {
            for (int page = 0; page < 5; page++)
            {
                string path = settings.Protocol switch
                {
                    AiProtocol.Ollama => "api/tags",
                    AiProtocol.AnthropicMessages => "models?limit=100" + (cursor is null ? "" : "&after_id=" + Uri.EscapeDataString(cursor)),
                    _ => "models"
                };
                using var request = AiWireFormat.Request(HttpMethod.Get, settings, path, apiKey);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                CheckStatus(response); await response.Content.LoadIntoBufferAsync(MaxResponseBytes, timeout.Token);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(timeout.Token), new JsonDocumentOptions { MaxDepth = 32 });
                string field = settings.Protocol == AiProtocol.Ollama ? "models" : "data", id = settings.Protocol == AiProtocol.Ollama ? "name" : "id";
                result.AddRange(document.RootElement.GetProperty(field).EnumerateArray().Select(item => item.GetProperty(id).GetString())
                    .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length <= 160 && !v.Any(char.IsControl) && !AiPrivacy.ContainsCredential(v)).Cast<string>());
                if (settings.Protocol != AiProtocol.AnthropicMessages || !document.RootElement.TryGetProperty("has_more", out var more) || !more.GetBoolean() || result.Count >= 500) break;
                cursor = document.RootElement.GetProperty("last_id").GetString();
                if (string.IsNullOrEmpty(cursor) || cursor.Length > 160 || cursor.Any(char.IsControl) || !cursors.Add(cursor))
                    throw new AiAnalysisException("模型列表分页信息无效，请手动填写模型。");
            }
            return result.Distinct(StringComparer.Ordinal).Take(500).ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AiAnalysisException("模型服务连接超时。"); }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new AiAnalysisException("无法读取模型列表，请检查地址和服务状态。"); }
    }
    public async IAsyncEnumerable<AiChatDelta> StreamAsync(AiChatRequest input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        input.Settings.Validate(); byte[] body = AiWireFormat.Body(input);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var request = AiWireFormat.Request(HttpMethod.Post, input.Settings, AiWireFormat.ChatPath(input.Settings.Protocol), input.ApiKey);
        request.Content = new ByteArrayContent(body); request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AiAnalysisException("模型服务响应超时。"); }
        catch (HttpRequestException) { throw new AiAnalysisException("无法连接模型服务，请检查地址和网络。"); }
        using (response)
        {
            CheckStatus(response); await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var parser = new AiStreamParser(input.Settings.Protocol, input.Tools, input.DisableTools); bool hasContent = false;
            await foreach (string json in ReadEventsAsync(stream, input.Settings.Protocol == AiProtocol.Ollama, timeout.Token, cancellationToken))
            {
                var delta = parser.Accept(json);
                if (delta.Text.Length > 0) { hasContent = true; yield return delta with { Done = false }; }
                if (delta.Done)
                {
                    var calls = parser.CompleteCalls();
                    if (!hasContent && calls.Count == 0) throw new AiAnalysisException("模型没有返回可显示的分析内容。");
                    foreach (var call in calls) yield return new(ToolCall: call);
                    if (input.Tools.Count > 0) foreach (var part in parser.CompleteContinuation()) yield return new(Continuation: part);
                    yield return delta with { Text = "" }; yield break;
                }
            }
            if (!parser.Finished) throw new AiAnalysisException("模型响应提前结束，结果不完整。");
            var remainingCalls = parser.CompleteCalls();
            if (!hasContent && remainingCalls.Count == 0) throw new AiAnalysisException("模型没有返回可显示的分析内容。");
            foreach (var call in remainingCalls) yield return new(ToolCall: call);
            if (input.Tools.Count > 0) foreach (var part in parser.CompleteContinuation()) yield return new(Continuation: part);
            yield return new(Done: true, Truncated: parser.Truncated);
        }
    }
    private static async IAsyncEnumerable<string> ReadEventsAsync(Stream stream, bool ndjson, CancellationToken timeout,
        [EnumeratorCancellation] CancellationToken caller)
    {
        int consumed = 0; var line = new StringBuilder(); var data = new StringBuilder(); byte[] buffer = new byte[8192];
        var decoder = new UTF8Encoding(false, true).GetDecoder(); char[] chars = new char[8192];
        while (true)
        {
            int read;
            try { read = await stream.ReadAsync(buffer, timeout); }
            catch (OperationCanceledException) when (!caller.IsCancellationRequested) { throw new AiAnalysisException("模型生成超时，当前结果尚未完成。"); }
            catch (IOException) { throw new AiAnalysisException("模型连接中断，当前结果尚未完成。"); }
            consumed += read; if (consumed > MaxResponseBytes) throw new AiAnalysisException("模型回复超过接收上限。");
            int count;
            try { count = decoder.GetChars(buffer.AsSpan(0, read), chars, flush: read == 0); }
            catch (DecoderFallbackException) { throw new AiAnalysisException("模型流包含无效文本，结果不完整。"); }
            for (int i = 0; i < count; i++)
            {
                if (chars[i] == '\r') continue;
                if (chars[i] != '\n') { line.Append(chars[i]); continue; }
                string text = line.ToString(); line.Clear();
                if (ndjson) { if (text.Length > 0) yield return text; }
                else if (text.StartsWith("data:", StringComparison.Ordinal)) data.AppendLine(text[5..].TrimStart());
                else if (text.Length == 0 && data.Length > 0) { yield return data.ToString().Trim(); data.Clear(); }
            }
            if (read == 0) break;
        }
        if (ndjson && line.Length > 0) yield return line.ToString();
        else if (!ndjson)
        {
            if (line.ToString().StartsWith("data:", StringComparison.Ordinal)) data.Append(line.ToString()[5..].TrimStart());
            if (data.Length > 0) yield return data.ToString().Trim();
        }
    }
    private static void CheckStatus(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new AiAnalysisException((int)response.StatusCode switch
        {
            401 or 403 => $"模型服务拒绝认证或访问（HTTP {(int)response.StatusCode}）。",
            404 => "模型或接口地址不存在（HTTP 404）。", 429 => "模型服务请求过于频繁（HTTP 429）。",
            _ => $"模型服务未完成请求（HTTP {(int)response.StatusCode}）。"
        });
    }
    public void Dispose() { if (ownsClient) client.Dispose(); }
}
