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
    public const string ArchiveExportFca = "archive.export.fca";
    public const string ArchiveOpenPackage = "archive.open.fca";
    public const string ArchivePlan = "archive.plan";
    public const string ArchiveApply = "archive.apply";
    public const string ArchiveReplaceScope = "archive.replace.scope";
    public const string DomainPortableExport = "archive.domain.portable.export";
    public const string DomainMarkdownExport = "archive.domain.markdown.export";
    public const string DomainPortableImport = "archive.domain.portable.import";
    public const string StartupWarning = "startup.warning";
    public const string StartupFailure = "startup.failure";
    public const string ArchiveCreate = "archive.create";
    public const string ArchiveRename = "archive.rename";
    public const string ArchiveDelete = "archive.delete";
    public const string ArchiveAccountAdd = "archive.account.add";
    public const string ArchiveAccountEdit = "archive.account.edit";
    public const string ArchiveAccountDelete = "archive.account.delete";
    public const string HistoryUndo = "history.undo";
    public const string HistoryUndoLimit = "history.undo-limit";
    public const string HistoryCollectCharacter = "history.collect.character";
    public const string HistoryCollectAbyss = "history.collect.abyss";
    public const string HistoryCollectChallenge = "history.collect.challenge";
    public const string PassportSave = "passport.save";
    public const string PassportDelete = "passport.delete";
    public const string PassportClear = "passport.clear";
    public const string PassportRefresh = "passport.refresh";
    public const string SettingsClearIconCache = "settings.icon-cache.clear";
    public const string SettingsUndoLimit = "settings.undo-limit";
    public const string SettingsExportDiagnostics = "settings.diagnostics.export";
}
