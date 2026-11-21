// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Ai;

public sealed record AiChatPreferences(string UserDisplayName = "You",
    string SystemPrompt = "你是一个乐于助人的原神账号分析助手。默认中文回答，先给结论，再给依据、限制及可行建议。",
    bool AllowLocalTools = false, int MaxToolCalls = 10)
{
    public void Validate()
    {
        if (UserDisplayName is null || string.IsNullOrWhiteSpace(UserDisplayName) || UserDisplayName.Length > 80 ||
            UserDisplayName.Any(char.IsControl) || SystemPrompt is null || SystemPrompt.Length > 16000 ||
            MaxToolCalls is < 0 or > 500 || AiPrivacy.ContainsCredential(UserDisplayName) || AiPrivacy.ContainsCredential(SystemPrompt))
            throw new AiAnalysisException("全局聊天设置无效：请检查称呼、提示词和工具次数（0～500），不要填入凭据。");
    }
}
// Ephemeral, model-origin protocol continuation. Never written to conversations or reports.
public sealed record AiModelContinuation(AiProtocol Protocol, string Json)
{
    public override string ToString() => "AI model continuation (content omitted)";
}
public sealed record AiToolDefinition(string Name, string Description, string ParametersJson);
public sealed record AiToolCall(string Id, string Name, string Arguments)
{
    public override string ToString() => "AI tool call (arguments omitted)";
}
public sealed record AiToolResult(string Content, string Summary, int Items);
public sealed record AiToolUse(string Name, string Summary, int Items);
public interface IAiReadOnlyToolRunner
{
    Task<AiToolResult> ExecuteAsync(AiToolCall call, PreparedAiContext context, CancellationToken token = default);
}
