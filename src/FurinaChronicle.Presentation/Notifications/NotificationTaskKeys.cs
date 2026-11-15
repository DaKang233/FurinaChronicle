// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Presentation.Notifications;

/// <summary>
/// Stable task keys for user-initiated operations. Progress and the final result of
/// one operation must reuse the same key so they update the same notification row.
/// </summary>
public static class NotificationTaskKeys
{
    public const string GachaRefreshManual = "gacha.refresh.manual";
    public const string GachaRefreshWindowsCache = "gacha.refresh.webcache";
    public const string GachaRefreshSToken = "gacha.refresh.stoken";
    public const string GachaImportUigf = "gacha.import.uigf";
    public const string GachaImportTeyvat = "gacha.import.teyvat";
    public const string GachaImportPortable = "gacha.import.portable";
    public const string GachaExportArchive = "gacha.export.archive";
    public const string GachaExportTable = "gacha.export.table";
    public const string GachaExportPortable = "gacha.export.portable";
    public const string GachaRecordCorrect = "gacha.record.correct";
    public const string GachaRecordDelete = "gacha.record.delete";
    public const string GachaRecordPurge = "gacha.record.purge";
    public const string GachaRecordUndo = "gacha.record.undo";
    public const string GachaIconCache = "gacha.cache.icons";
    public const string StartupWarning = "startup.warning";
    public const string StartupFailure = "startup.failure";
}
