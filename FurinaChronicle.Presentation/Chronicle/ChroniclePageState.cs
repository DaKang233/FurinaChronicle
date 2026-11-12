// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Presentation.Chronicle;

public enum ChronicleLoadState
{
    Idle = 0,
    LoadingFirstPage = 1,
    LoadingMore = 2,
    Loaded = 3,
    Empty = 4,
    Error = 5,
}

/// <summary>
/// Why the current browsing snapshot can no longer be trusted. Invalidated and
/// expired cursors both require an explicit reload instead of silently mixing
/// generations, pages or time bases.
/// </summary>
public enum ChronicleStaleReason
{
    None = 0,
    CursorInvalidated = 1,
    CursorExpired = 2,
    CursorInvalid = 3,
    CursorVersionMismatch = 4,
    GenerationChanged = 5,
}

/// <summary>
/// Pure Chronicle page state machine: first page, cursor pagination, empty,
/// error and stale-snapshot handling. It holds no UI or platform types and is
/// fully driven by <see cref="ChroniclePage"/> results, so the same rules are
/// testable and reusable by a future WinUI host.
/// </summary>
public sealed class ChroniclePageState
{
    private readonly List<ChronicleEntry> entries = [];
    private readonly HashSet<string> entryIds = new(StringComparer.Ordinal);
    private bool pending;
    private bool pendingInitial;

    public ChronicleQuery? Query { get; private set; }

    public ChronicleLoadState LoadState { get; private set; } = ChronicleLoadState.Idle;

    public IReadOnlyList<ChronicleEntry> Entries => entries;

    public int LoadedCount => entries.Count;

    public string? NextCursor { get; private set; }

    public string? GenerationId { get; private set; }

    public bool HasMore => NextCursor is not null &&
        StaleReason == ChronicleStaleReason.None;

    public ChronicleStaleReason StaleReason { get; private set; } =
        ChronicleStaleReason.None;

    public bool RequiresReload => StaleReason != ChronicleStaleReason.None;

    public string? ErrorMessage { get; private set; }

    /// <summary>Developer-facing detail; never rendered as primary user text.</summary>
    public string? DiagnosticMessage { get; private set; }

    public bool IsLoading => LoadState is
        ChronicleLoadState.LoadingFirstPage or ChronicleLoadState.LoadingMore;

    public bool IsEmpty => LoadState == ChronicleLoadState.Empty;

    public bool BeginInitialLoad(ChronicleQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        entries.Clear();
        entryIds.Clear();
        Query = query;
        NextCursor = null;
        GenerationId = null;
        StaleReason = ChronicleStaleReason.None;
        ErrorMessage = null;
        DiagnosticMessage = null;
        LoadState = ChronicleLoadState.LoadingFirstPage;
        pending = true;
        pendingInitial = true;
        return true;
    }

    public bool BeginLoadMore()
    {
        if (IsLoading || RequiresReload || NextCursor is null || Query is null)
        {
            return false;
        }

        LoadState = ChronicleLoadState.LoadingMore;
        ErrorMessage = null;
        DiagnosticMessage = null;
        pending = true;
        pendingInitial = false;
        return true;
    }

    public void ApplyPage(ChroniclePage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!pending)
        {
            return;
        }
        if (page.Error != ChronicleQueryError.None)
        {
            ApplyQueryError(page.Error, page.ErrorMessage);
            return;
        }

        bool initial = pendingInitial;
        if (initial)
        {
            entries.Clear();
            entryIds.Clear();
            GenerationId = page.GenerationId;
            StaleReason = ChronicleStaleReason.None;
        }
        else if (!string.Equals(
                     GenerationId,
                     page.GenerationId,
                     StringComparison.Ordinal))
        {
            // A continuation page from another generation must never be merged
            // into the snapshot already on screen.
            ApplyStale(
                ChronicleStaleReason.GenerationChanged,
                page.ErrorMessage);
            return;
        }

        foreach (ChronicleEntry entry in page.Entries)
        {
            if (entryIds.Add(entry.EntryId.Value))
            {
                entries.Add(entry);
            }
        }

        NextCursor = page.NextCursor;
        pending = false;
        pendingInitial = false;
        ErrorMessage = null;
        DiagnosticMessage = null;
        LoadState = entries.Count == 0
            ? ChronicleLoadState.Empty
            : ChronicleLoadState.Loaded;
    }

    public void ApplyQueryError(
        ChronicleQueryError error,
        string? diagnostic = null)
    {
        if (error == ChronicleQueryError.None)
        {
            return;
        }

        pending = false;
        pendingInitial = false;
        DiagnosticMessage = diagnostic;
        switch (error)
        {
            case ChronicleQueryError.CursorInvalidated:
                // Destructive changes make every loaded page of the old
                // generation invalid, so the list is cleared rather than left
                // readable with mixed provenance.
                ClearEntries();
                NextCursor = null;
                GenerationId = null;
                ApplyStale(ChronicleStaleReason.CursorInvalidated, diagnostic);
                break;
            case ChronicleQueryError.CursorExpired:
                NextCursor = null;
                ApplyStale(ChronicleStaleReason.CursorExpired, diagnostic);
                break;
            case ChronicleQueryError.InvalidCursor:
                NextCursor = null;
                ApplyStale(ChronicleStaleReason.CursorInvalid, diagnostic);
                break;
            case ChronicleQueryError.CursorVersionMismatch:
                NextCursor = null;
                ApplyStale(
                    ChronicleStaleReason.CursorVersionMismatch,
                    diagnostic);
                break;
            case ChronicleQueryError.UnsupportedFilter:
                NextCursor = null;
                StaleReason = ChronicleStaleReason.None;
                LoadState = ChronicleLoadState.Error;
                ErrorMessage = "当前筛选条件不受游戏历程查询支持，请调整条件后重试。";
                break;
            default:
                LoadState = ChronicleLoadState.Error;
                ErrorMessage = "读取游戏历程时遇到未知查询错误，请重新载入。";
                break;
        }
    }

    public void ApplyTransportError(string? diagnostic = null)
    {
        pending = false;
        pendingInitial = false;
        DiagnosticMessage = diagnostic;
        ErrorMessage = "读取游戏历程失败，请稍后重试。";
        LoadState = entries.Count == 0
            ? ChronicleLoadState.Error
            : ChronicleLoadState.Loaded;
    }

    public void ApplyCancelled()
    {
        pending = false;
        pendingInitial = false;
        LoadState = entries.Count == 0
            ? ChronicleLoadState.Idle
            : ChronicleLoadState.Loaded;
    }

    public void Reset()
    {
        ClearEntries();
        Query = null;
        NextCursor = null;
        GenerationId = null;
        StaleReason = ChronicleStaleReason.None;
        ErrorMessage = null;
        DiagnosticMessage = null;
        LoadState = ChronicleLoadState.Idle;
        pending = false;
        pendingInitial = false;
    }

    private void ApplyStale(
        ChronicleStaleReason reason,
        string? diagnostic = null)
    {
        StaleReason = reason;
        NextCursor = null;
        DiagnosticMessage = diagnostic;
        LoadState = ChronicleLoadState.Error;
        ErrorMessage = ChronicleStatusText.DescribeStale(reason);
    }

    private void ClearEntries()
    {
        entries.Clear();
        entryIds.Clear();
    }
}
