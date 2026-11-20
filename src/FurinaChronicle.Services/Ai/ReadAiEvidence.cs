// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Services.Ai;

public sealed record AiEvidenceView(AiEvidenceFact Fact, string Availability);

public sealed class ReadAiEvidence(IGachaRecordRepository gacha, IArchiveDomainHistoryStore history)
{
    public async Task<AiEvidenceView> ExecuteAsync(AiLocalEvidence source, CancellationToken cancellationToken = default)
    {
        if (source.EntityKind is "gacha" or "gacha_record")
        {
            var rows = await gacha.GetByExternalRecordIdsAsync(source.AccountId, [source.EntityId], cancellationToken);
            return new(source.Fact, rows.Any(item => item.GameAccountId == source.AccountId && item.ExternalRecordId == source.EntityId)
                ? "来源抽卡记录仍在本地。下方为本次准备资料时的摘录；若已纠正，请重新准备。"
                : "来源记录已不存在或不可读取；下方仅为本次会话的摘录，不能据此恢复记录。");
        }
        for (int offset = 0; offset < 10000; offset += 200)
        {
            int total; bool found;
            if (source.EntityKind.Contains("item", StringComparison.OrdinalIgnoreCase))
            {
                var page = await history.GetItemObservationPageAsync(new(source.AccountId, Offset: offset, Limit: 200), cancellationToken);
                total = page.TotalCount; found = page.Items.Any(item => item.ObservationId.ToString() == source.EntityId &&
                    (source.EntryKey is null || item.Entries.Any(entry => entry.StableItemKey == source.EntryKey)));
            }
            else if (source.EntityKind.Contains("challenge", StringComparison.OrdinalIgnoreCase) || source.EntityKind.Contains("abyss", StringComparison.OrdinalIgnoreCase))
            {
                var page = await history.GetChallengeObservationPageAsync(new(source.AccountId, Offset: offset, Limit: 200), cancellationToken);
                total = page.TotalCount; found = page.Items.Any(item => item.ObservationId.ToString() == source.EntityId);
            }
            else if (source.EntityKind.Contains("manual", StringComparison.OrdinalIgnoreCase))
            {
                var page = await history.GetManualAcquisitionPageAsync(new(source.AccountId, Offset: offset, Limit: 200), cancellationToken);
                total = page.TotalCount; found = page.Items.Any(item => item.Acquisition.AcquisitionId.ToString() == source.EntityId);
            }
            else return new(source.Fact, "派生节点的基础引用保留在本机；当前读模型不支持直接定位此类别，请到对应业务页核对。");
            if (found) return new(source.Fact, "已定位本地来源记录。下方是本次准备资料时的摘录，未从其他快照补造字段。");
            if (offset + 200 >= total) return new(source.Fact, "未找到本地来源记录；它可能已删除，下方仅为会话摘录。");
        }
        return new(source.Fact, "超过有界查验窗口，尚无法确认来源是否存在；可在对应业务页核对。");
    }
}
