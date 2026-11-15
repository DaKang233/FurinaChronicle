// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Presentation.Gacha;

// Platform hosts own the file picker and the confirmation container. Everything below is
// platform-neutral planning state: no MAUI, WinUI, file-system or SQLite object crosses it.
public enum GachaImportTargetScope
{
    CurrentAccount = 0,
    ArchiveAccounts = 1
}

public sealed record GachaImportModeOption(GachaImportMode Mode)
{
    public bool IsFull => Mode == GachaImportMode.Full;

    public string Label => IsFull ? "全量导入：覆盖同记录" : "仅补充";

    public string Description => IsFull
        ? "只覆盖来源明确包含的同记录并保留修订；不删除来源缺失的本地记录。"
        : "只新增本地没有的记录；已有同记录不覆盖。不删除来源缺失的本地记录。";

    public string ToggleLabel => IsFull ? "切换为仅补充" : "切换为全量导入：覆盖同记录";

    public static IReadOnlyList<GachaImportModeOption> Options { get; } =
    [
        new(GachaImportMode.Incremental),
        new(GachaImportMode.Full)
    ];
}

// One instance per page. The page exposes this single mode so every import entry shares it.
public sealed class GachaImportModeState : INotifyPropertyChanged
{
    private GachaImportMode mode = GachaImportMode.Incremental;

    public GachaImportMode Mode => mode;

    public GachaImportModeOption Option => new(mode);

    public bool IsFull => mode == GachaImportMode.Full;

    public string Label => Option.Label;

    public string Description => Option.Description;

    public string ToggleLabel => Option.ToggleLabel;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? Changed;

    public void SetMode(GachaImportMode value)
    {
        if (value is not (GachaImportMode.Incremental or GachaImportMode.Full))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (mode == value)
        {
            return;
        }

        mode = value;
        PropertyChanged?.Invoke(this, new(nameof(Mode)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle() => SetMode(IsFull ? GachaImportMode.Incremental : GachaImportMode.Full);

    // The import flow takes this snapshot once and reuses it on the confirmation page.
    public GachaImportMode Snapshot() => mode;
}

// A named way to reopen the chosen file. Reading happens at most twice per flow: once for the
// header/version check and once through the production reader. Records are never materialized
// as UI rows.
public sealed record GachaImportSource(string FileName, Func<CancellationToken, Task<Stream>> OpenReadAsync)
{
    public static GachaImportSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new(
            Path.GetFileName(path),
            _ => Task.FromResult<Stream>(new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan)));
    }
}

// Target IDs are copied from the page snapshot when the flow starts and are never re-read from
// the page while the flow runs.
public sealed record GachaImportTargetRequest(GachaImportTargetScope Scope, Guid ArchiveId)
{
    public Guid? GameAccountId { get; init; }
}

public sealed record GachaImportSourceAccountSummary(string Uid, int ParsedRecordCount, int ErrorCount)
{
    public GameServerRegion Region { get; init; } = GameServerRegion.Unknown;
}

// Only what the production reader actually reports: file-level counts, per-UID record counts and
// parse issues. No record content is retained for preview purposes.
public sealed record GachaImportSourceSummary
{
    public required GachaImportSource Source { get; init; }

    public string FileName => Source.FileName;

    public required string FormatVersion { get; init; }

    public string FormatLabel => GachaImportText.FormatLabel(FormatVersion);

    public int TotalRecordCount { get; init; }

    public required IReadOnlyList<GachaImportSourceAccountSummary> Accounts { get; init; }

    public int ErrorCount { get; init; }

    public int ParsedRecordCount => Accounts.Sum(account => account.ParsedRecordCount);
}

public sealed record GachaImportPlanTarget(
    GachaImportTargetScope Scope,
    Guid ArchiveId,
    string ArchiveName,
    Guid? GameAccountId,
    string? AccountName,
    string? Uid,
    GameServerRegion? Region)
{
    public string ScopeLabel => Scope == GachaImportTargetScope.CurrentAccount
        ? "当前所选账号"
        : "当前档案全部账号";

    public string? AccountIdentity => Scope == GachaImportTargetScope.CurrentAccount && Uid is not null
        ? $"{AccountName ?? "游戏账号"} · UID {Uid} · " +
            GachaImportText.RegionLabel(Region ?? GameServerRegion.Unknown)
        : null;
}

public sealed record GachaImportSourceAccountPreview(
    string Uid,
    GameServerRegion Region,
    int ParsedRecordCount,
    int ErrorCount)
{
    public bool ExistingAccount { get; init; }

    public string? ExistingAccountLabel { get; init; }

    public bool MatchesTarget { get; init; }

    public bool WillCreateAccount { get; init; }

    public bool Ignored { get; init; }

    public bool Skipped { get; init; }
}

public sealed record GachaImportPlan
{
    public required GachaImportSourceSummary Source { get; init; }

    public required GachaImportTargetRequest Request { get; init; }

    public required GachaImportPlanTarget Target { get; init; }

    // Fixed when the flow starts. Committing uses this value, not the page's current mode.
    public required GachaImportMode Mode { get; init; }

    public IReadOnlyList<GachaImportSourceAccountPreview> Accounts { get; init; } = [];

    public int ImportableRecordCount { get; init; }

    public int IgnoredRecordCount { get; init; }

    public int SkippedRecordCount { get; init; }

    public string? BlockMessage { get; init; }

    public bool CanExecute => BlockMessage is null;

    public GachaImportModeOption ModeOption => new(Mode);

    public string ConfirmationText => GachaImportText.BuildConfirmationText(this);
}

public sealed record GachaImportSourceReadResult(GachaImportSourceSummary? Summary, string? FailureMessage)
{
    public bool Succeeded => Summary is not null;
}

public sealed record GachaImportPlanResult(GachaImportPlan? Plan, string? FailureMessage)
{
    public bool Succeeded => Plan is not null;
}

public enum GachaImportExecutionStatus
{
    Succeeded = 0,
    Rejected = 1,
    Failed = 2
}

public sealed record GachaImportExecution(
    GachaImportExecutionStatus Status,
    GachaImportResult? Result,
    string Message)
{
    public bool IsSuccess => Status == GachaImportExecutionStatus.Succeeded;

    public bool WasRejected => Status == GachaImportExecutionStatus.Rejected;
}

// Read side used by planning: it looks up archives and account copies, never records.
public interface IGachaImportPlanningSource
{
    Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameAccount>> GetAccountsAsync(
        Guid playerArchiveId,
        CancellationToken cancellationToken = default);
}

// Thin seam over the production ImportUigfGachaRecords use case so planning tests can prove that
// invalid input and stale targets never reach a write.
public interface IGachaUigfImporter
{
    Task<GachaImportResult> ExecuteAsync(
        Stream source,
        Guid playerArchiveId,
        Guid? targetGameAccountId,
        GachaImportMode mode,
        CancellationToken cancellationToken = default);
}
