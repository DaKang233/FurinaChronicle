// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Infrastructure.Ai;

internal sealed class AiToolStreamCalls(IReadOnlyList<AiToolDefinition> definitions, bool disabled)
{
    private sealed class Call
    {
        public string Id = "", Name = ""; public StringBuilder Arguments = new(); public bool HasDeltas;
    }
    private readonly Dictionary<string, Call> calls = [];
    private Call Get(string key)
    {
        if (disabled || definitions.Count == 0) throw new AiAnalysisException("模型请求了未授权工具调用。");
        if (key.Length > 256 || calls.Count >= 64 && !calls.ContainsKey(key)) throw new AiAnalysisException("本次模型工具调用超过有界数量。");
        if (!calls.TryGetValue(key, out var value)) calls[key] = value = new();
        return value;
    }
    public void Chat(JsonElement tools)
    {
        foreach (var item in tools.EnumerateArray())
        {
            int index = item.GetProperty("index").GetInt32(); if (index is < 0 or > 63) throw new AiAnalysisException("工具序号无效。");
            var call = Get(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (item.TryGetProperty("id", out var id)) call.Id += id.GetString();
            if (item.TryGetProperty("type", out var type) && type.GetString() != "function") throw new AiAnalysisException("未授权此工具类型。");
            if (item.TryGetProperty("function", out var function))
            {
                if (function.TryGetProperty("name", out var name)) call.Name += name.GetString();
                if (function.TryGetProperty("arguments", out var args)) Append(call, args.GetString() ?? "");
            }
            CheckLength(call);
        }
    }
    public void ResponseItem(JsonElement item)
    {
        string key = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : item.GetProperty("call_id").GetString() ?? "";
        var call = Get(key); string callId = item.GetProperty("call_id").GetString() ?? "", name = item.GetProperty("name").GetString() ?? "";
        if (call.Id.Length > 0 && call.Id != callId || call.Name.Length > 0 && call.Name != name) throw new AiAnalysisException("工具标识在响应中发生变化。");
        call.Id = callId; call.Name = name;
        if (item.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String && args.GetString() is { Length: > 0 } text) SetFinal(call, text);
        CheckLength(call);
    }
    public void ResponseArguments(JsonElement root, bool final)
    {
        string key = root.GetProperty("item_id").GetString() ?? "";
        if (!calls.TryGetValue(key, out var call)) throw new AiAnalysisException("工具响应缺少起始标识。");
        string text = root.GetProperty(final ? "arguments" : "delta").GetString() ?? "";
        if (final) SetFinal(call, text); else Append(call, text);
    }
    public void AnthropicStart(int index, JsonElement block)
    {
        if (index is < 0 or > 63) throw new AiAnalysisException("工具序号无效。");
        var call = Get(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        call.Id = block.GetProperty("id").GetString() ?? ""; call.Name = block.GetProperty("name").GetString() ?? "";
        if (block.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object && input.EnumerateObject().Any()) SetFinal(call, input.GetRawText());
        CheckLength(call);
    }
    public void AnthropicArguments(int index, string text)
    {
        if (!calls.TryGetValue(index.ToString(System.Globalization.CultureInfo.InvariantCulture), out var call)) throw new AiAnalysisException("工具响应缺少起始标识。");
        Append(call, text);
    }
    private static void CheckLength(Call call)
    {
        if (call.Id.Length > 256 || call.Name.Length > 100 || call.Arguments.Length > 8192) throw new AiAnalysisException("工具标识或参数过长。");
    }
    private static void Append(Call call, string text)
    { call.HasDeltas = true; call.Arguments.Append(text); CheckLength(call); }
    private static void SetFinal(Call call, string text)
    {
        if (call.Arguments.Length > 0 && call.Arguments.ToString() != text) throw new AiAnalysisException("工具参数在响应中不一致。");
        call.Arguments.Clear(); call.Arguments.Append(text); CheckLength(call);
    }
    public IReadOnlyList<AiToolCall> Complete()
    {
        var result = new List<AiToolCall>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in calls.Values)
        {
            string args = call.Arguments.Length == 0 ? "{}" : call.Arguments.ToString();
            if (call.Id.Length == 0 || call.Id.Any(char.IsControl) || AiPrivacy.ContainsCredential(call.Id) ||
                !ids.Add(call.Id) || !definitions.Any(d => d.Name == call.Name) || AiPrivacy.ContainsCredential(args))
                throw new AiAnalysisException("模型返回未授权工具、无效标识或不安全参数。");
            using var json = JsonDocument.Parse(args, new JsonDocumentOptions { MaxDepth = 8 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new AiAnalysisException("工具参数必须为对象。");
            result.Add(new(call.Id, call.Name, args));
        }
        return result;
    }
}
