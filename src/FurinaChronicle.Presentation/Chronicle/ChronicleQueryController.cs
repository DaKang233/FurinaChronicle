// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Presentation.Chronicle;

public enum ChronicleLoadOutcome
{
    Loaded = 0,
    Empty = 1,
    Stale = 2,
    Failed = 3,
    Superseded = 4,
}

/// <summary>Drives formal Chronicle queries; superseded responses cannot mutate newer query state.</summary>
public sealed class ChronicleQueryController(IChronicleQueryService queryService)
{
    private readonly IChronicleQueryService queryService = queryService ??
        throw new ArgumentNullException(nameof(queryService));
    private readonly object gate = new();
    private CancellationTokenSource? activeRequest;
    private int requestGeneration;

    public ChroniclePageState State { get; } = new();

    public async Task<ChronicleLoadOutcome> LoadFirstPageAsync(
        ChronicleQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previous;
        int generation;
        lock (gate)
        {
            previous = activeRequest;
            generation = ++requestGeneration;
            activeRequest = source;
            State.BeginInitialLoad(query);
        }
        // Invalidate the old generation before cancellation can run its continuations.
        // Keep cancellation callbacks outside the state lock.
        TryCancel(previous);
        return await ExecuteAsync(query, source, generation).ConfigureAwait(false);
    }

    public async Task<ChronicleLoadOutcome> LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        ChronicleQuery query;
        CancellationTokenSource source;
        int generation;
        lock (gate)
        {
            if (!State.BeginLoadMore()) return ChronicleLoadOutcome.Superseded;
            query = State.Query! with { Cursor = State.NextCursor };
            generation = ++requestGeneration;
            source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            activeRequest = source;
        }
        return await ExecuteAsync(query, source, generation).ConfigureAwait(false);
    }

    private async Task<ChronicleLoadOutcome> ExecuteAsync(
        ChronicleQuery query, CancellationTokenSource source, int generation)
    {
        try
        {
            ChroniclePage page = await queryService.QueryAsync(query, source.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (generation != requestGeneration) return ChronicleLoadOutcome.Superseded;
                State.ApplyPage(page);
                return Outcome(page);
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            lock (gate)
            {
                if (generation == requestGeneration) State.ApplyCancelled();
            }
            return ChronicleLoadOutcome.Superseded;
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                if (generation == requestGeneration) State.ApplyTransportError(exception.Message);
            }
            return ChronicleLoadOutcome.Failed;
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(activeRequest, source)) activeRequest = null;
            }
            source.Dispose();
        }
    }

    /// <summary>Cancel any in-flight request without discarding loaded entries.</summary>
    public void CancelActive()
    {
        CancellationTokenSource? source;
        lock (gate) source = activeRequest;
        TryCancel(source);
    }

    public void Reset()
    {
        CancellationTokenSource? source;
        lock (gate)
        {
            source = activeRequest;
            activeRequest = null;
            requestGeneration++;
            State.Reset();
        }
        TryCancel(source);
    }

    private ChronicleLoadOutcome Outcome(ChroniclePage page)
    {
        if (page.Error != ChronicleQueryError.None)
            return State.RequiresReload ? ChronicleLoadOutcome.Stale : ChronicleLoadOutcome.Failed;
        return State.LoadState == ChronicleLoadState.Empty ? ChronicleLoadOutcome.Empty : ChronicleLoadOutcome.Loaded;
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
