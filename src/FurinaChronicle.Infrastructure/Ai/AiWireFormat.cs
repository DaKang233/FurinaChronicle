// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Infrastructure.Ai;

internal static class AiWireFormat
{
    public static string ChatPath(AiProtocol p) => AiProtocols.ChatPath(p);
    public static HttpRequestMessage Request(HttpMethod method, AiServiceSettings settings, string path, string? key)
    {
        if (key?.Any(char.IsControl) == true || key?.Length > 4096) throw new AiAnalysisException("API 密钥格式无效。");
        var request = new HttpRequestMessage(method, new Uri(settings.Endpoint, path));
        if (settings.Protocol == AiProtocol.AnthropicMessages)
        {
            request.Headers.Add("anthropic-version", "2023-06-01");
            if (!string.IsNullOrWhiteSpace(key)) request.Headers.Add("x-api-key", key);
        }
        else if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }
    public static byte[] Body(AiChatRequest request)
    {
        var settings = request.Settings;
        if (request.Messages.Count is < 1 or > 256 || request.Messages.Any(m => m.Role is not ("system" or "user" or "assistant" or "tool") || m.Content.Length > 1_200_000))
            throw new AiAnalysisException("发送消息的数量或长度无效。");
        foreach (var m in request.Messages)
            if (m.Images is { Count: > 0 } images && (!settings.SupportsImages || m.Role != "user" || images.Count > 8 ||
                images.Any(i => i.MediaType is not ("image/png" or "image/jpeg" or "image/webp") || i.Base64.Length > 3_000_000)))
                throw new AiAnalysisException("图片能力或附件格式/大小无效。");
        if (request.Tools.Count > 10 || settings.Protocol == AiProtocol.Ollama && request.Tools.Count > 0 ||
            request.Messages.Any(m => m.Role == "tool" && (string.IsNullOrEmpty(m.ToolCallId) || m.ToolCallId.Length > 256) ||
                m.ToolCalls is { Count: > 0 } && m.Role != "assistant")) throw new AiAnalysisException("工具协议或消息无效。");
        if (request.Tools.Select(t=>t.Name).Distinct(StringComparer.Ordinal).Count()!=request.Tools.Count) throw new AiAnalysisException("工具定义重复。");
        var payload = new JsonObject { ["model"] = settings.Model.Trim(), ["stream"] = true };
        if (settings.Protocol == AiProtocol.OpenAiResponses)
        {
            payload["input"] = Messages(request.Messages, settings.Protocol);
            payload["max_output_tokens"] = settings.MaxOutputTokens;
            payload["store"] = false;
        }
        else if (settings.Protocol == AiProtocol.AnthropicMessages)
        {
            if (!request.Messages.Any(m => m.Role == "user")) throw new AiAnalysisException("Messages 请求需要用户消息。");
            payload["messages"] = Messages(request.Messages.Where(m => m.Role != "system"), settings.Protocol);
            var system = new JsonArray();
            foreach (var m in request.Messages.Where(m => m.Role == "system")) system.Add(Text("text", m.Content));
            if (system.Count > 0) payload["system"] = system;
            payload["max_tokens"] = settings.MaxOutputTokens;
        }
        else
        {
            payload["messages"] = Messages(request.Messages, settings.Protocol);
            if (settings.Protocol == AiProtocol.Ollama)
            {
                payload["think"] = false; payload["keep_alive"] = 0;
                var options = new JsonObject { ["num_ctx"] = settings.OllamaContextTokens, ["num_predict"] = settings.MaxOutputTokens, ["temperature"] = 0.2 };
                if (settings.CpuOnly) options["num_gpu"] = 0;
                payload["options"] = options;
            }
            else
            {
                payload["max_tokens"] = settings.MaxOutputTokens; payload["temperature"] = 0.2;
                if (settings.DisableCompatibleThinking) payload["thinking"] = new JsonObject { ["type"] = "disabled" };
            }
        }
        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var definition in request.Tools)
            {
                if (definition.Name.Length is < 1 or > 64 || definition.Name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_') || definition.ParametersJson.Length>20000)
                    throw new AiAnalysisException("工具定义无效。");
                var function = new JsonObject { ["name"] = definition.Name, ["description"] = definition.Description,
                    [settings.Protocol == AiProtocol.AnthropicMessages ? "input_schema" : "parameters"] = JsonNode.Parse(definition.ParametersJson) };
                if (settings.Protocol == AiProtocol.OpenAiCompatible) tools.Add(new JsonObject { ["type"] = "function", ["function"] = function });
                else { if (settings.Protocol == AiProtocol.OpenAiResponses) function["type"] = "function"; tools.Add(function); }
            }
            payload["tools"] = tools;
            if (settings.Protocol == AiProtocol.OpenAiResponses) payload["include"] = new JsonArray("reasoning.encrypted_content");
            if (request.DisableTools) payload["tool_choice"] = settings.Protocol == AiProtocol.AnthropicMessages
                ? new JsonObject { ["type"] = "none" } : JsonValue.Create("none");
        }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (bytes.Length > 24 * 1024 * 1024) throw new AiAnalysisException("请求超过安全长度，请缩小资料或对话范围。");
        return bytes;
    }
    private static JsonObject Continuation(AiModelContinuation part,AiProtocol protocol)
    {
        if(part.Protocol!=protocol||part.Json.Length>256000)throw new AiAnalysisException("模型续接协议或长度无效。");
        return JsonNode.Parse(part.Json)?.AsObject()??throw new AiAnalysisException("模型续接无效。");
    }
    private static JsonObject Text(string type, string text) => new() { ["type"] = type, ["text"] = text };
    private static JsonArray Messages(IEnumerable<AiMessage> input, AiProtocol protocol)
    {
        var result = new JsonArray();
        foreach (var m in input)
        {
            if (m.Continuation is {Count:>0} continuation && protocol==AiProtocol.OpenAiResponses)
                foreach(var part in continuation) { var node=Continuation(part,protocol); if(node["type"]?.GetValue<string>()!="reasoning")throw new AiAnalysisException("续接类型无效。");result.Add(node); }
            if (protocol == AiProtocol.OpenAiResponses && m.Role == "tool")
            { result.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = m.ToolCallId, ["output"] = m.Content }); continue; }
            if (protocol == AiProtocol.OpenAiResponses && m.ToolCalls is { Count: > 0 })
            {
                if (m.Content.Length > 0) result.Add(new JsonObject { ["role"] = "assistant", ["content"] = m.Content });
                foreach (var call in m.ToolCalls) result.Add(new JsonObject { ["type"] = "function_call", ["call_id"] = call.Id, ["name"] = call.Name, ["arguments"] = call.Arguments });
                continue;
            }
            if (protocol == AiProtocol.AnthropicMessages && m.Role == "tool")
            {
                result.Add(new JsonObject { ["role"] = "user", ["content"] = new JsonArray { new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = m.ToolCallId, ["content"] = m.Content } } }); continue;
            }
            var item = new JsonObject { ["role"] = m.Role, ["content"] = m.Content };
            if(protocol==AiProtocol.OpenAiCompatible && m.Continuation is {Count:>0})
                foreach(var part in m.Continuation) item["reasoning_content"]=Continuation(part,protocol)["reasoning_content"]?.DeepClone();
            if (protocol == AiProtocol.OpenAiCompatible && m.Role == "tool") item["tool_call_id"] = m.ToolCallId;
            if (protocol == AiProtocol.OpenAiCompatible && m.ToolCalls is { Count: > 0 })
            {
                var calls = new JsonArray(); foreach (var call in m.ToolCalls) calls.Add(new JsonObject { ["id"] = call.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments } });
                item["tool_calls"] = calls;
            }
            if (protocol == AiProtocol.AnthropicMessages || m.Images is { Count: > 0 } && protocol != AiProtocol.Ollama)
            {
                var content = new JsonArray { Text(protocol == AiProtocol.OpenAiResponses ? "input_text" : "text", m.Content) };
                foreach (var image in m.Images ?? [])
                    content.Add(protocol switch
                    {
                        AiProtocol.OpenAiResponses => new JsonObject { ["type"] = "input_image", ["image_url"] = $"data:{image.MediaType};base64,{image.Base64}" },
                        AiProtocol.AnthropicMessages => new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = image.MediaType, ["data"] = image.Base64 } },
                        _ => new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{image.MediaType};base64,{image.Base64}" } }
                    });
                if(protocol==AiProtocol.AnthropicMessages && m.Continuation is {Count:>0})
                    foreach(var part in m.Continuation.Reverse())
                    {var node=Continuation(part,protocol);if(node["type"]?.GetValue<string>() is not ("thinking" or "redacted_thinking"))throw new AiAnalysisException("续接类型无效。");content.Insert(0,node);}
                if (protocol == AiProtocol.AnthropicMessages && m.ToolCalls is { Count: > 0 })
                {
                    if (m.Content.Length == 0) {for(int i=content.Count-1;i>=0;i--)if(content[i]?["type"]?.GetValue<string>()=="text")content.RemoveAt(i);}
                    foreach (var call in m.ToolCalls) content.Add(new JsonObject { ["type"] = "tool_use", ["id"] = call.Id, ["name"] = call.Name, ["input"] = JsonNode.Parse(call.Arguments) });
                }
                item["content"] = content;
            }
            else if (protocol == AiProtocol.Ollama && m.Images is { Count: > 0 })
            {
                var images = new JsonArray(); foreach (var image in m.Images) images.Add(image.Base64); item["images"] = images;
            }
            result.Add(item);
        }
        return result;
    }
}
