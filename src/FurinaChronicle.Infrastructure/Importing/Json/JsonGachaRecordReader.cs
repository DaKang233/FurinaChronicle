// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.Json;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Infrastructure.Importing.Json;

public sealed class JsonGachaRecordReader : IGachaRecordReader
{
    private const string SupportedTimeFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<GachaRecordReadResult> ReadAsync(Stream source, Guid gameAccountId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanRead)
        {
            throw new ArgumentException("输入流不可读取。", nameof(source));
        }

        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException("游戏账号 ID 不能为空。", nameof(gameAccountId));
        }

        JsonGachaImportDto? document;

        try
        {
            document = await JsonSerializer.DeserializeAsync<JsonGachaImportDto>(source, SerializerOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new GachaRecordImportFormatException("文件不是有效的祈愿记录 JSON。", exception);
        }

        if (document?.List is null)
        {
            throw new GachaRecordImportFormatException("JSON 中缺少有效的 list 数组。");
        }

        var records = new List<GachaRecord>();
        var errors = new List<GachaRecordImportError>();

        for (int index = 0; index < document.List.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JsonGachaRecordDto? dto = document.List[index];

            // 对用户显示时采用从 1 开始的序号。
            int recordIndex = index + 1;

            if (dto is null)
            {
                errors.Add(new GachaRecordImportError(recordIndex, "null_record", "记录内容不能为空。"));
                continue;
            }

            string? externalRecordId = dto.Id?.Trim();
            string? itemName = dto.Name?.Trim();
            string? rankTypeText = dto.RankType?.Trim();
            string? timeText = dto.Time?.Trim();

            if (string.IsNullOrWhiteSpace(externalRecordId))
            {
                errors.Add(new GachaRecordImportError(recordIndex, "missing_id", "记录 ID 不能为空。"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(itemName))
            {
                errors.Add(new GachaRecordImportError(recordIndex, "missing_name", "物品名称不能为空。"));
                continue;
            }

            if (!int.TryParse(rankTypeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rankType) || rankType is < 3 or > 5)
            {
                errors.Add(new GachaRecordImportError(recordIndex, "invalid_rank_type", $"星级「{rankTypeText}」无效。"));
                continue;
            }

            if (!DateTimeOffset.TryParseExact(timeText, SupportedTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset time))
            {
                errors.Add(new GachaRecordImportError(recordIndex, "invalid_time", $"时间「{timeText}」格式无效。"));
                continue;
            }

            records.Add(new GachaRecord(gameAccountId, externalRecordId, itemName, rankType, time));
        }

        return new GachaRecordReadResult(records.ToArray(), errors.ToArray());
    }
}