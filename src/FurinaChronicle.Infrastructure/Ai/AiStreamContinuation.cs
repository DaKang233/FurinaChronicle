// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Infrastructure.Ai;

internal sealed class AiStreamContinuation(AiProtocol protocol)
{
    private readonly Dictionary<int, JsonObject> anthropic = [];
    private readonly Dictionary<string, string> responses = [];
    private readonly StringBuilder chat = new();
    public void Chat(JsonElement delta)
    {
        if (delta.TryGetProperty("reasoning_content", out var content) && content.ValueKind == JsonValueKind.String) chat.Append(content.GetString());
        if (chat.Length > 256000) throw new AiAnalysisException("模型续接内容超过有界长度。");
    }
    public void ResponseItem(JsonElement item)
    {
        if (item.GetProperty("type").GetString() != "reasoning") return;
        if (!item.TryGetProperty("encrypted_content", out var encrypted) || encrypted.ValueKind != JsonValueKind.String) return;
        string key = item.GetProperty("id").GetString() ?? "";
        if(key.Length is < 1 or > 256 || key.Any(char.IsControl))throw new AiAnalysisException("模型续接标识无效。");
        var value = new JsonObject { ["id"] = key, ["type"] = "reasoning", ["encrypted_content"] = encrypted.GetString(), ["summary"] = new JsonArray() };
        if (item.TryGetProperty("summary", out var summary)) value["summary"] = JsonNode.Parse(summary.GetRawText());
        responses[key] = value.ToJsonString(); if (responses.Count > 32) throw new AiAnalysisException("模型续接片段过多。");
    }
    public void AnthropicStart(JsonElement root)
    {
        var block = root.GetProperty("content_block"); string? type = block.GetProperty("type").GetString();
        if (type is not ("thinking" or "redacted_thinking")) return;
        int index = root.GetProperty("index").GetInt32();
        if (index is < 0 or > 63) throw new AiAnalysisException("模型续接序号无效。");
        anthropic[index] = JsonNode.Parse(block.GetRawText())!.AsObject();
    }
    public void AnthropicDelta(JsonElement root)
    {
        var delta = root.GetProperty("delta"); string? type = delta.GetProperty("type").GetString();
        if (type is not ("thinking_delta" or "signature_delta")) return;
        if (!anthropic.TryGetValue(root.GetProperty("index").GetInt32(), out var block)) throw new AiAnalysisException("模型续接片段缺少起点。");
        string key = type == "thinking_delta" ? "thinking" : "signature";
        block[key] = (block[key]?.GetValue<string>() ?? "") + delta.GetProperty(key).GetString();
        if (block.ToJsonString().Length > 256000) throw new AiAnalysisException("模型续接片段过长。");
    }
    public IReadOnlyList<AiModelContinuation> Complete()
    {
        IEnumerable<string> items = protocol switch
        {
            AiProtocol.OpenAiCompatible when chat.Length > 0 => [new JsonObject { ["reasoning_content"] = chat.ToString() }.ToJsonString()],
            AiProtocol.OpenAiResponses => responses.Values,
            AiProtocol.AnthropicMessages => anthropic.OrderBy(p=>p.Key).Select(p=>p.Value.ToJsonString()),
            _ => []
        };
        return items.Select(j=>new AiModelContinuation(protocol,j)).ToArray();
    }
}
