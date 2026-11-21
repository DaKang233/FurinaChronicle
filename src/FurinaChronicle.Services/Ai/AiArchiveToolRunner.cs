// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace FurinaChronicle.Services.Ai;

public static class AiArchiveTools
{
    public static readonly IReadOnlyList<AiToolDefinition> Definitions = Array.AsReadOnly(new AiToolDefinition[]
    {
        new("search_archive_evidence", "筛选当前已附加的本地档案证据快照；不是完整数据库搜索。dataset/query 可为空，limit 为 1～20。",
            """{"type":"object","properties":{"query":{"type":"string"},"dataset":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":20}},"required":["query","dataset","limit"],"additionalProperties":false}"""),
        new("read_archive_evidence", "按当前资料的 E 编号读取白名单证据，并检查其本地来源可用性；不能恢复已删事实。",
            """{"type":"object","properties":{"reference":{"type":"string"}},"required":["reference"],"additionalProperties":false}"""),
        new("get_gacha_statistics", "读取当前附加范围的确定性抽卡统计快照，不刷新游戏数据。",
            """{"type":"object","properties":{},"additionalProperties":false}""")
    });
}

public sealed class AiArchiveToolRunner(ReadAiEvidence reader) : IAiReadOnlyToolRunner
{
    public async Task<AiToolResult> ExecuteAsync(AiToolCall call, PreparedAiContext context, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (call.Arguments.Length > 8192 || AiPrivacy.ContainsCredential(call.Arguments)) throw new AiAnalysisException("工具参数无效或包含凭据。");
        try
        {
            using var document = JsonDocument.Parse(call.Arguments, new JsonDocumentOptions { MaxDepth = 8 });
            var args = document.RootElement;
            if (args.ValueKind != JsonValueKind.Object) throw new AiAnalysisException("工具参数必须是对象。");
            void Fields(params string[] names)
            {
                var actual = args.EnumerateObject().Select(p => p.Name).ToArray();
                if (actual.Length != names.Length || actual.Distinct().Count() != actual.Length || actual.Any(n => !names.Contains(n)))
                    throw new AiAnalysisException("工具参数不属于此只读查询。");
            }
            switch (call.Name)
            {
                case "search_archive_evidence":
                    Fields("query", "dataset", "limit");
                    string query = context.SanitizeText(args.GetProperty("query").GetString() ?? "", 200);
                    string dataset = args.GetProperty("dataset").GetString() ?? "";
                    int limit = args.GetProperty("limit").GetInt32();
                    if (dataset.Length > 40 || limit is < 1 or > 20) throw new AiAnalysisException("工具筛选或条数无效。");
                    var facts = context.Packet.Evidence.Where(f => (dataset.Length == 0 || f.Dataset == dataset) &&
                        (query.Length == 0 || f.Description.Contains(query, StringComparison.OrdinalIgnoreCase) || f.Reference.Equals(query, StringComparison.OrdinalIgnoreCase))).Take(limit).ToArray();
                    return Result(new { coverage = "prepared_snapshot_only", evidence = facts }, "筛选当前资料证据", facts.Length);
                case "read_archive_evidence":
                    Fields("reference"); string reference = args.GetProperty("reference").GetString() ?? "";
                    if (reference.Length > 12) throw new AiAnalysisException("证据编号无效。");
                    var source = context.Evidence.FirstOrDefault(e => e.Fact.Reference == reference);
                    if (source is null) return Result(new { available = false, message = "此编号不在当前已附加范围内。" }, "未找到当前范围证据", 0);
                    var view = await reader.ExecuteAsync(source, token);
                    return Result(view, "核验当前资料证据 " + reference, 1);
                case "get_gacha_statistics":
                    Fields(); return Result(new { coverage = context.Packet.Coverage, statistics = context.Packet.GachaStatistics }, "读取抽卡统计快照", context.Packet.GachaStatistics.Count);
                default: throw new AiAnalysisException("未授权此工具；仅允许当前附加范围内的本地只读查询。");
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new AiAnalysisException("工具参数无法解析，未执行查询。"); }
    }
    private static AiToolResult Result(object value, string summary, int count)
    {
        string content = JsonSerializer.Serialize(value, AiJson.Options);
        if (content.Length > 24000) throw new AiAnalysisException("工具结果超过本轮有界长度，请缩小附加资料范围。");
        if (AiPrivacy.ContainsCredential(content)) throw new AiAnalysisException("工具结果疑似包含凭据，已阻止发送。");
        return new(content, summary, count);
    }
}
