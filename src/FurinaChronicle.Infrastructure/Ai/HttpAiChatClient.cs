// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net.Http.Headers;
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
    }
    public async Task<IReadOnlyList<string>> ListModelsAsync(AiServiceSettings settings, string? apiKey,
        CancellationToken cancellationToken = default)
    {
        settings.Validate(requireModel: false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = Request(HttpMethod.Get, settings,
            settings.Protocol == AiProtocol.Ollama ? "api/tags" : "models", apiKey);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            CheckStatus(response);
            await response.Content.LoadIntoBufferAsync(MaxResponseBytes, timeout.Token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(timeout.Token),
                new JsonDocumentOptions { MaxDepth = 32 });
            string field = settings.Protocol == AiProtocol.Ollama ? "models" : "data";
            string id = settings.Protocol == AiProtocol.Ollama ? "name" : "id";
            return document.RootElement.GetProperty(field).EnumerateArray().Select(item => item.GetProperty(id).GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 160 && !value.Any(char.IsControl) && !AiPrivacy.ContainsCredential(value))
                .Cast<string>().Distinct(StringComparer.Ordinal).Take(500).ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new AiAnalysisException("模型服务连接超时。"); }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new AiAnalysisException("无法读取模型列表，请检查地址和服务状态。"); }
    }
    public async IAsyncEnumerable<AiChatDelta> StreamAsync(AiChatRequest input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        input.Settings.Validate();
        if (input.Messages.Count is < 1 or > 20 || input.Messages.Any(item => item.Role is not ("system" or "user" or "assistant") || item.Content.Length > 1_200_000))
            throw new AiAnalysisException("发送消息的数量或长度无效。");
        var messages = input.Messages.Select(item => new { role = item.Role, content = item.Content }).ToArray();
        object payload = input.Settings.Protocol == AiProtocol.Ollama
            ? new { model = input.Settings.Model.Trim(), messages, stream = true, think = false, keep_alive = 0,
                options = new Dictionary<string, object> { ["num_ctx"] = input.Settings.OllamaContextTokens,
                    ["num_predict"] = input.Settings.MaxOutputTokens, ["temperature"] = 0.2 } }
            : new { model = input.Settings.Model.Trim(), messages, stream = true,
                max_tokens = input.Settings.MaxOutputTokens, temperature = 0.2 };
        if (input.Settings.Protocol == AiProtocol.Ollama && input.Settings.CpuOnly)
        {
            var root = JsonSerializer.SerializeToNode(payload)!;
            root["options"]!["num_gpu"] = 0;
            payload = root;
        }
        if (input.Settings.Protocol == AiProtocol.OpenAiCompatible && input.Settings.DisableCompatibleThinking)
        {
            var root = JsonSerializer.SerializeToNode(payload)!;
            root["thinking"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "disabled" };
            payload = root;
        }
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (body.Length > 8 * 1024 * 1024) throw new AiAnalysisException("请求超过安全长度，请缩小资料或对话范围。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var request = Request(HttpMethod.Post, input.Settings,
            input.Settings.Protocol == AiProtocol.Ollama ? "api/chat" : "chat/completions", input.ApiKey);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new AiAnalysisException("模型服务响应超时。"); }
        catch (HttpRequestException) { throw new AiAnalysisException("无法连接模型服务，请检查地址和网络。"); }
        using (response)
        {
            CheckStatus(response);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            int consumed = 0;
            bool finished = false, truncated = false, hasContent = false;
            var line = new StringBuilder();
            var eventData = new StringBuilder();
            byte[] buffer = new byte[8192];
            var decoder = Encoding.UTF8.GetDecoder();
            char[] chars = new char[8192];
            while (true)
            {
                int read;
                try { read = await stream.ReadAsync(buffer, timeout.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { throw new AiAnalysisException("模型生成超时，当前结果尚未完成。"); }
                catch (IOException) { throw new AiAnalysisException("模型连接中断，当前结果尚未完成。"); }
                if (read == 0) break;
                consumed += read;
                if (consumed > MaxResponseBytes) throw new AiAnalysisException("模型回复超过接收上限。");
                int count = decoder.GetChars(buffer.AsSpan(0, read), chars, flush: false);
                for (int i = 0; i < count; i++)
                {
                    if (chars[i] == '\r') continue;
                    if (chars[i] != '\n') { line.Append(chars[i]); continue; }
                    string text = line.ToString(); line.Clear();
                    string? json = null;
                    if (input.Settings.Protocol == AiProtocol.Ollama) { if (text.Length > 0) json = text; }
                    else if (text.StartsWith("data:", StringComparison.Ordinal)) eventData.AppendLine(text[5..].TrimStart());
                    else if (text.Length == 0 && eventData.Length > 0) { json = eventData.ToString().Trim(); eventData.Clear(); }
                    if (json is null) continue;
                    if (json == "[DONE]")
                    {
                        if (!hasContent) throw new AiAnalysisException("模型没有返回可显示的分析内容。");
                        yield return new(Done: true, Truncated: truncated);
                        yield break;
                    }
                    var delta = Parse(json, input.Settings.Protocol);
                    if (delta.Text.Length > 0) { hasContent = true; yield return delta with { Done = false }; }
                    if (delta.Done) { finished = true; truncated |= delta.Truncated; }
                    if (finished && input.Settings.Protocol == AiProtocol.Ollama)
                    {
                        if (!hasContent) throw new AiAnalysisException("模型没有返回可显示的分析内容。");
                        yield return new(Done: true, Truncated: truncated); yield break;
                    }
                }
            }
            if (line.Length > 0 || eventData.Length > 0)
            {
                string json = input.Settings.Protocol == AiProtocol.Ollama ? line.ToString() :
                    eventData + (line.ToString().StartsWith("data:", StringComparison.Ordinal) ? line.ToString()[5..] : "");
                if (json.Trim() == "[DONE]") finished = true;
                else if (!string.IsNullOrWhiteSpace(json))
                {
                    var last = Parse(json, input.Settings.Protocol);
                    if (last.Text.Length > 0) { hasContent = true; yield return last with { Done = false }; }
                    finished |= last.Done; truncated |= last.Truncated;
                }
            }
            if (!finished || !hasContent) throw new AiAnalysisException("模型响应提前结束，结果不完整。");
            yield return new(Done: true, Truncated: truncated);
        }
    }
    private static AiChatDelta Parse(string json, AiProtocol protocol)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out _)) throw new AiAnalysisException("模型服务报告生成失败。");
            if (protocol == AiProtocol.Ollama)
            {
                string content = "";
                if (root.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("tool_calls", out var tools) && tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() > 0)
                        throw new AiAnalysisException("模型请求了工具调用；此功能只支持只读文本分析。");
                    if (message.TryGetProperty("content", out var text)) content = text.GetString() ?? "";
                }
                bool done = root.TryGetProperty("done", out var flag) && flag.GetBoolean();
                return new(content, done, root.TryGetProperty("done_reason", out var reason) && reason.GetString() == "length");
            }
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return new();
            var choice = choices[0];
            string value = "";
            if (choice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("tool_calls", out var tools) && tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() > 0)
                    throw new AiAnalysisException("模型请求了工具调用；此功能只支持只读文本分析。");
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) value = content.GetString() ?? "";
            }
            string? finish = choice.TryGetProperty("finish_reason", out var status) && status.ValueKind == JsonValueKind.String ? status.GetString() : null;
            return new(value, finish is not null, finish is "length" or "content_filter");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new AiAnalysisException("模型返回了无法解析的数据，结果尚未完成。"); }
    }
    private static HttpRequestMessage Request(HttpMethod method, AiServiceSettings settings, string path, string? key)
    {
        if (key?.Any(char.IsControl) == true) throw new AiAnalysisException("API 密钥格式无效。");
        var request = new HttpRequestMessage(method, new Uri(settings.Endpoint, path));
        if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }
    private static void CheckStatus(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new AiAnalysisException((int)response.StatusCode switch
        {
            401 or 403 => $"模型服务拒绝认证或访问（HTTP {(int)response.StatusCode}）。",
            404 => "模型或接口地址不存在（HTTP 404）。",
            429 => "模型服务请求过于频繁（HTTP 429）。",
            _ => $"模型服务未完成请求（HTTP {(int)response.StatusCode}）。"
        });
    }
    public void Dispose() { if (ownsClient) client.Dispose(); }
}
