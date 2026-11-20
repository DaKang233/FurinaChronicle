// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Infrastructure.Ai;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Tests.Ai;

public sealed class HttpAiChatClientTests
{
    [Theory]
    [InlineData(AiProtocol.OpenAiCompatible)]
    [InlineData(AiProtocol.Ollama)]
    public async Task ReadsStreamingUnicodeAndUsesOnlyDedicatedAuthorization(AiProtocol protocol)
    {
        string? captured = null; HttpRequestMessage? request = null;
        using var http = new HttpClient(new Handler(async input =>
        {
            captured = await input.Content!.ReadAsStringAsync(); request = input;
            string body = protocol == AiProtocol.Ollama
                ? "{\"message\":{\"content\":\"你好\"},\"done\":false}\n{\"done\":true,\"done_reason\":\"stop\"}\n"
                : "data: {\"choices\":[{\"delta\":{\"content\":\"你好\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
            return new(HttpStatusCode.OK) { Content = new StreamContent(new TinyStream(Encoding.UTF8.GetBytes(body))) };
        }));
        using var client = new HttpAiChatClient(http);
        var settings = new AiServiceSettings(protocol, "https://example.invalid/v1", "synthetic-model", CpuOnly: true, DisableCompatibleThinking: true);
        var deltas = new List<AiChatDelta>();
        await foreach (var delta in client.StreamAsync(new(settings, [new("user", "合成资料")], "synthetic-key"))) deltas.Add(delta);
        Assert.Equal("你好", string.Concat(deltas.Select(item => item.Text)));
        Assert.True(deltas[^1].Done); Assert.False(deltas[^1].Truncated);
        Assert.Equal("Bearer", request!.Headers.Authorization!.Scheme);
        Assert.DoesNotContain(request.Headers, item => item.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("synthetic-key", captured);
        using var doc = JsonDocument.Parse(captured!);
        Assert.True(doc.RootElement.GetProperty("stream").GetBoolean());
        if (protocol == AiProtocol.OpenAiCompatible) Assert.Equal("disabled", doc.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        if (protocol == AiProtocol.Ollama)
        { Assert.False(doc.RootElement.TryGetProperty("thinking", out _)); Assert.Equal(0, doc.RootElement.GetProperty("keep_alive").GetInt32()); Assert.Equal(0, doc.RootElement.GetProperty("options").GetProperty("num_gpu").GetInt32()); }
    }
    [Theory]
    [InlineData(401)]
    [InlineData(302)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task ErrorsDoNotEchoProviderBodyOrCredential(int status)
    {
        int calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            { Content = new StringContent("synthetic-sensitive-error-body") }); }));
        using var client = new HttpAiChatClient(http);
        var error = await Assert.ThrowsAsync<AiAnalysisException>(async () =>
        { await foreach (var _ in client.StreamAsync(new(new(Model: "m"), [new("user", "hello")], "synthetic-key"))) { } });
        Assert.DoesNotContain("sensitive-error", error.ToString()); Assert.DoesNotContain("synthetic-key", error.ToString()); Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n")]
    [InlineData("data: not-json\n\n")]
    [InlineData("data: {\"error\":{\"message\":\"synthetic-sensitive-error-body\"}}\n\n")]
    [InlineData("data: {\"choices\":[{\"delta\":{\"tool_calls\":[{}]}}]}\n\n")]
    public async Task MalformedInterruptedOrToolResponsesCannotBeMarkedComplete(string body)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) })));
        using var client = new HttpAiChatClient(http);
        var error = await Assert.ThrowsAsync<AiAnalysisException>(async () =>
        { await foreach (var delta in client.StreamAsync(new(new(Model: "m"), [new("user", "hello")], null))) Assert.False(delta.Done); });
        Assert.DoesNotContain("synthetic-sensitive-error-body", error.ToString());
    }
    [Fact]
    public async Task LengthFinishIsExplicitlyIncomplete()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n") })));
        using var client = new HttpAiChatClient(http); AiChatDelta? last = null;
        await foreach (var item in client.StreamAsync(new(new(Model: "m"), [new("user", "hi")], null))) last = item;
        Assert.True(last!.Done); Assert.True(last.Truncated);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request); }
    private sealed class TinyStream(byte[] bytes) : MemoryStream(bytes)
    { public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken); }
}
