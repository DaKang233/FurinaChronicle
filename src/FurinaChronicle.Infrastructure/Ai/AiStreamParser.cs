// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Infrastructure.Ai;

/// <summary>Per-request protocol state; buffers bounded continuations in memory and never executes tools.</summary>
internal sealed class AiStreamParser(AiProtocol protocol, IReadOnlyList<AiToolDefinition>? definitions = null, bool disabled = false)
{
    public bool Finished { get; private set; }
    public bool Truncated { get; private set; }
    private bool anthropicStopReason, expectsCalls;
    private readonly AiStreamContinuation continuation = new(protocol);
    public IReadOnlyList<AiModelContinuation> CompleteContinuation() => continuation.Complete();
    private readonly AiToolStreamCalls calls = new(definitions ?? [], disabled || protocol == AiProtocol.Ollama);
    public IReadOnlyList<AiToolCall> CompleteCalls()
    {
        try { var result = calls.Complete(); if (expectsCalls && result.Count == 0) throw new AiAnalysisException("模型缺少完整工具调用。"); return result; }
        catch (JsonException) { throw new AiAnalysisException("工具参数无法解析。"); }
    }
    public AiChatDelta Accept(string json)
    {
        if (json == "[DONE]")
        {
            if (protocol != AiProtocol.OpenAiCompatible) throw new AiAnalysisException("模型缺少此协议的正式终止事件。");
            Finished = true; return new(Done: true, Truncated: Truncated);
        }
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new AiAnalysisException("模型服务报告生成失败。");
            return protocol switch
            {
                AiProtocol.OpenAiResponses => Responses(root),
                AiProtocol.AnthropicMessages => Anthropic(root),
                AiProtocol.Ollama => Ollama(root),
                _ => Completions(root)
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        { throw new AiAnalysisException("模型返回了无法解析的数据，结果尚未完成。"); }
    }
    private static AiAnalysisException Tools() => new("模型请求了工具调用；此功能只支持只读文本分析。");
    private void RejectChatTools(JsonElement item)
    {
        if (item.TryGetProperty("function_call", out var f) && f.ValueKind != JsonValueKind.Null) throw Tools();
        if (item.TryGetProperty("tool_calls", out var t) && t.ValueKind == JsonValueKind.Array && t.GetArrayLength() > 0) calls.Chat(t);
    }
    private AiChatDelta Completions(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return new();
        var choice = choices[0]; string text = "";
        if (choice.TryGetProperty("delta", out var delta))
        {
            continuation.Chat(delta); RejectChatTools(delta);
            if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String) text = c.GetString() ?? "";
        }
        if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
        {
            string? stop = reason.GetString();
            if (stop == "function_call") throw Tools();
            if (stop is not ("stop" or "length" or "content_filter" or "tool_calls")) throw new AiAnalysisException("模型返回了未知终止原因。");
            expectsCalls |= stop == "tool_calls"; Finished = true; Truncated |= stop is "length" or "content_filter";
        }
        return new(text, Truncated: Truncated);
    }
    private AiChatDelta Ollama(JsonElement root)
    {
        string text = "";
        if (root.TryGetProperty("message", out var message))
        {
            RejectChatTools(message);
            if (message.TryGetProperty("content", out var value)) text = value.GetString() ?? "";
        }
        bool done = root.TryGetProperty("done", out var flag) && flag.GetBoolean();
        Finished |= done; Truncated |= root.TryGetProperty("done_reason", out var reason) && reason.GetString() == "length";
        return new(text, done, Truncated);
    }
    private void CheckResponseItem(JsonElement item, bool complete = true)
    {
        if (item.GetProperty("type").GetString() == "function_call") calls.ResponseItem(item);
        else if (item.GetProperty("type").GetString() is not ("message" or "reasoning")) throw Tools();
        else if(complete) continuation.ResponseItem(item);
    }
    private AiChatDelta Responses(JsonElement root)
    {
        string? type = root.GetProperty("type").GetString();
        switch (type)
        {
            case "response.output_text.delta": return new(root.GetProperty("delta").GetString() ?? "");
            case "response.refusal.delta": Truncated = true; return new(root.GetProperty("delta").GetString() ?? "");
            case "response.output_item.added": CheckResponseItem(root.GetProperty("item"), false); break;
            case "response.output_item.done": CheckResponseItem(root.GetProperty("item")); break;
            case "response.function_call_arguments.delta": calls.ResponseArguments(root, false); break;
            case "response.function_call_arguments.done": calls.ResponseArguments(root, true); break;
            case "error":
            case "response.failed":
            case "response.cancelled": throw new AiAnalysisException("模型服务报告生成失败或取消，结果尚未完成。");
            case "response.completed":
            case "response.incomplete":
                var response = root.GetProperty("response");
                if (response.TryGetProperty("output", out var output)) foreach (var item in output.EnumerateArray()) CheckResponseItem(item);
                if (response.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                    throw new AiAnalysisException("模型服务报告生成失败。");
                if (response.TryGetProperty("status", out var status) && status.GetString() != (type == "response.completed" ? "completed" : "incomplete"))
                    throw new AiAnalysisException("模型终止状态不一致。");
                Finished = true; Truncated |= type == "response.incomplete";
                return new(Done: true, Truncated: Truncated);
        }
        return new();
    }
    private void CheckAnthropicBlock(JsonElement block, int index)
    {
        if (block.GetProperty("type").GetString() == "tool_use") calls.AnthropicStart(index, block);
        else if (block.GetProperty("type").GetString() is not ("text" or "thinking" or "redacted_thinking")) throw Tools();
    }
    private AiChatDelta Anthropic(JsonElement root)
    {
        switch (root.GetProperty("type").GetString())
        {
            case "error": throw new AiAnalysisException("模型服务报告生成失败。");
            case "message_start":
                if (root.GetProperty("message").TryGetProperty("content", out var blocks)) { int index = 0; foreach (var block in blocks.EnumerateArray()) CheckAnthropicBlock(block, index++); }
                break;
            case "content_block_start":
                var start = root.GetProperty("content_block"); continuation.AnthropicStart(root); CheckAnthropicBlock(start, root.GetProperty("index").GetInt32());
                if (start.GetProperty("type").GetString() == "text") return new(start.GetProperty("text").GetString() ?? "");
                break;
            case "content_block_delta":
                var delta = root.GetProperty("delta"); continuation.AnthropicDelta(root);
                if (delta.GetProperty("type").GetString() == "text_delta") return new(delta.GetProperty("text").GetString() ?? "");
                if (delta.GetProperty("type").GetString() == "input_json_delta") calls.AnthropicArguments(root.GetProperty("index").GetInt32(), delta.GetProperty("partial_json").GetString() ?? "");
                break;
            case "message_delta":
                if (root.GetProperty("delta").TryGetProperty("stop_reason", out var stop) && stop.ValueKind == JsonValueKind.String)
                {
                    string? reason = stop.GetString();
                    
                    if (reason is not ("end_turn" or "stop_sequence" or "max_tokens" or "model_context_window_exceeded" or "refusal" or "tool_use"))
                        throw new AiAnalysisException("模型返回了不支持的终止原因。");
                    anthropicStopReason = true; expectsCalls |= reason == "tool_use"; Truncated |= reason is "max_tokens" or "model_context_window_exceeded" or "refusal";
                }
                break;
            case "message_stop":
                if (!anthropicStopReason) throw new AiAnalysisException("模型缺少正式终止原因，结果尚未完成。");
                Finished = true; return new(Done: true, Truncated: Truncated);
        }
        return new();
    }
}
