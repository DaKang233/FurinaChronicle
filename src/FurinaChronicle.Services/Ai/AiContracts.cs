// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace FurinaChronicle.Services.Ai;

// These persisted values and names also participate in existing device-secret slots.
public enum AiProtocol { OpenAiCompatible = 0, Ollama = 1, OpenAiResponses = 2, AnthropicMessages = 3 }
public sealed record AiProtocolChoice(AiProtocol Value, string Label);
public static class AiProtocols
{
    public static string Label(AiProtocol protocol) => Choices(protocol).First(c=>c.Value==protocol).Label;
    public static string ChatPath(AiProtocol protocol) => protocol switch
    {
        AiProtocol.OpenAiCompatible=>"chat/completions", AiProtocol.OpenAiResponses=>"responses",
        AiProtocol.AnthropicMessages=>"messages", AiProtocol.Ollama=>"api/chat",
        _=>throw new AiAnalysisException("不支持此模型协议。")
    };
    public static IReadOnlyList<AiProtocolChoice> Choices(AiProtocol existing) => existing == AiProtocol.Ollama
        ? [new(AiProtocol.OpenAiCompatible, "OpenAI Chat Completions"), new(AiProtocol.OpenAiResponses, "OpenAI Responses"), new(AiProtocol.AnthropicMessages, "Anthropic Messages"), new(AiProtocol.Ollama, "Ollama（旧配置）")]
        : [new(AiProtocol.OpenAiCompatible, "OpenAI Chat Completions"), new(AiProtocol.OpenAiResponses, "OpenAI Responses"), new(AiProtocol.AnthropicMessages, "Anthropic Messages")];
}

public sealed record AiServiceSettings(
    AiProtocol Protocol = AiProtocol.OpenAiCompatible,
    string BaseAddress = "http://127.0.0.1:11434/v1",
    string Model = "",
    int ContextCharacters = 24000,
    int MaxOutputTokens = 2048,
    int OllamaContextTokens = 4096,
    bool CpuOnly = false,
    bool DisableCompatibleThinking = false,
    bool SupportsImages = false,
    Guid? ProviderId = null,
    Guid? ModelProfileId = null)
{
    public Uri Endpoint
    {
        get
        {
            if (!Uri.TryCreate(BaseAddress.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new AiAnalysisException("服务地址必须是没有账号、查询参数或片段的 HTTP(S) 地址。");
            if (uri.Scheme == "http" && !IsLocal(uri))
                throw new AiAnalysisException("外部模型服务必须使用 HTTPS；明文地址仅支持回环或私网 IP。");
            return uri;
        }
    }
    public static bool IsLocal(Uri uri)
    {
        if (uri.IsLoopback) return true;
        if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip)) return false;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal) return true;
        byte[] b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31);
    }
    public void Validate(bool requireModel = true)
    {
        _ = Endpoint;
        if (!Enum.IsDefined(Protocol) || ContextCharacters is < 4000 or > 1_000_000 ||
            MaxOutputTokens is < 128 or > 8192 || OllamaContextTokens is < 2048 or > 131072)
            throw new AiAnalysisException("模型设置中的范围无效。");
        if (requireModel && (string.IsNullOrWhiteSpace(Model) || Model.Length > 160 ||
            Model.Any(c => char.IsControl(c)) || AiPrivacy.ContainsCredential(Model)))
            throw new AiAnalysisException("请填写有效的模型名称。");
    }
}

public sealed class AiAnalysisException(string message) : Exception(message);
public sealed record AiMessage(string Role, string Content, IReadOnlyList<AiImage>? Images = null, IReadOnlyList<AiToolCall>? ToolCalls = null, string? ToolCallId = null, IReadOnlyList<AiModelContinuation>? Continuation = null);
public sealed record AiChatDelta(string Text = "", bool Done = false, bool Truncated = false, AiToolCall? ToolCall = null, AiModelContinuation? Continuation = null);

// Deliberately not a record: secrets must never appear in generated ToString output.
public sealed class AiChatRequest(AiServiceSettings settings, IReadOnlyList<AiMessage> messages, string? apiKey, IReadOnlyList<AiToolDefinition>? tools = null, bool disableTools = false)
{
    public AiServiceSettings Settings { get; } = settings;
    public IReadOnlyList<AiToolDefinition> Tools { get; } = tools ?? [];
    public bool DisableTools { get; } = disableTools;
    public IReadOnlyList<AiMessage> Messages { get; } = messages;
    [JsonIgnore] public string? ApiKey { get; } = apiKey;
    public override string ToString() => "AI chat request (credentials omitted)";
}

public interface IAiChatClient
{
    Task<IReadOnlyList<string>> ListModelsAsync(AiServiceSettings settings, string? apiKey, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AiChatDelta> StreamAsync(AiChatRequest request, CancellationToken cancellationToken = default);
}

public interface IAiSecretStore
{
    Task<string?> GetAsync(string slot, CancellationToken cancellationToken = default);
    Task SetAsync(string slot, string? value, CancellationToken cancellationToken = default);
}

public interface IAiConfigurationStore
{
    Task<AiServiceSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AiServiceSettings settings, CancellationToken cancellationToken = default);
    Task<string?> GetApiKeyAsync(AiServiceSettings settings, CancellationToken cancellationToken = default);
    Task SetApiKeyAsync(AiServiceSettings settings, string? key, CancellationToken cancellationToken = default);
}

[Flags]
public enum AiDatasets { None = 0, Gacha = 1, Items = 2, Challenges = 4, Chronicle = 8, All = 15 }
public sealed record AiAnalysisScope(Guid ArchiveId, Guid? AccountId = null,
    DateOnly? From = null, DateOnly? To = null, AiDatasets Datasets = AiDatasets.All, int MaxEvidence = 120, bool AllowArchiveUids = true)
{
    public DateTimeOffset? FromUtc => From is { } d ? new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
    public DateTimeOffset? ToUtcExclusive => To is { } d ? new(d.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
    public void Validate()
    {
        if (ArchiveId == Guid.Empty || AccountId == Guid.Empty || Datasets == AiDatasets.None ||
            (Datasets & ~AiDatasets.All) != 0 || MaxEvidence is < 12 or > 2000 ||
            From is { } start && To is { } end && start > end || To == DateOnly.MaxValue)
            throw new AiAnalysisException("请选择有效的档案、数据类别和日期范围。");
    }
}

// Only these display-safe types can be serialized into the model context.
public sealed record AiEvidenceFact(string Reference, string Account, string Dataset,
    string? OccurredDate, string? ObservedDate, string? FetchedDate, string Origin,
    string Coverage, string Description);
public sealed record AiPoolSummary(string Account, string Pool, int Pulls, int FiveStars, int FourStars, int ThreeStars,
    double? MeanFiveStarInterval, double? MeanLimitedFiveStarInterval, int? SinceFiveStar, int? SinceFourStar);
public sealed record AiContextPacket(string Game, string IdentityMode, string? FromUtcDate, string? ThroughUtcDate,
    IReadOnlyList<string> Accounts, IReadOnlyList<AiPoolSummary> GachaStatistics,
    IReadOnlyList<string> Coverage, IReadOnlyList<AiEvidenceFact> Evidence);

// These identifiers never enter AiContextPacket or the HTTP request.
public sealed record AiLocalEvidence(AiEvidenceFact Fact, Guid AccountId, string EntityKind, string EntityId, string? EntryKey = null)
{
    public string DisplayLabel => $"[{Fact.Reference}] {Fact.Account} · {Fact.Description}";
}
public sealed class PreparedAiContext(AiContextPacket packet, IReadOnlyList<AiLocalEvidence> evidence,
    IReadOnlyList<string> privateIdentifiers, IReadOnlyDictionary<string, string>? uidAliases = null)
{
    public AiContextPacket Packet { get; } = packet;
    public IReadOnlyList<AiLocalEvidence> Evidence { get; } = evidence;
    internal IReadOnlyList<string> PrivateIdentifiers { get; } = privateIdentifiers;
    public string SanitizeText(string text, int maximumLength = 16000)
    {
        if (uidAliases is not null)
            foreach (var entry in uidAliases.OrderByDescending(item => item.Key.Length))
                text = text.Replace(entry.Key, entry.Value, StringComparison.Ordinal);
        return AiPrivacy.Sanitize(text, PrivateIdentifiers, maximumLength);
    }
    public bool IncludesUids => Packet.IdentityMode == "archive_uid_with_confirmation";
    public string Json => JsonSerializer.Serialize(Packet, AiJson.Options);
    public AiContextState Snapshot() => new(Packet, Evidence, PrivateIdentifiers, uidAliases ?? new Dictionary<string,string>());
    public static PreparedAiContext Restore(AiContextState state) => new(state.Packet, state.Evidence, state.PrivateIdentifiers, state.UidAliases);
}
public static class AiJson
{
    public static readonly JsonSerializerOptions Options = new()
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true, MaxDepth = 32,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
}
