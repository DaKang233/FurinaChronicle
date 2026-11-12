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

/// <summary>
/// Drives <see cref="ChroniclePageState"/> against the formal
/// <see cref="IChronicleQueryService"/>. Starting a new query cancels the
/// previous request and invalidates its result, so a slow response cannot
/// overwrite a newer scope, timeline or filter selection.
/// </summary>
public sealed class ChronicleQueryController
{
    private readonly IChronicleQueryService queryService;
    private CancellationTokenSource? activeRequest;
    private int requestGeneration;

    public ChronicleQueryController(IChronicleQueryService queryService)
    {
        this.queryService = queryService ??
            throw new ArgumentNullException(nameof(queryService));
    }

    public ChroniclePageState State { get; } = new();

    public async Task<ChronicleLoadOutcome> LoadFirstPageAsync(
        ChronicleQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        CancelActiveRequest();
        int generation = ++requestGeneration;
        CancellationTokenSource source =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        activeRequest = source;
        State.BeginInitialLoad(query);

        try
        {
            ChroniclePage page = await queryService
                .QueryAsync(query, source.Token)
                .ConfigureAwait(false);
            if (generation != requestGeneration)
            {
                return ChronicleLoadOutcome.Superseded;
            }

            State.ApplyPage(page);
            return Outcome(page);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            if (generation == requestGeneration)
            {
                State.ApplyCancelled();
            }
            return ChronicleLoadOutcome.Superseded;
        }
        catch (Exception exception)
        {
            if (generation == requestGeneration)
            {
                State.ApplyTransportError(exception.Message);
            }
            return ChronicleLoadOutcome.Failed;
        }
        finally
        {
            Release(source);
        }
    }

    public async Task<ChronicleLoadOutcome> LoadMoreAsync(
        CancellationToken cancellationToken = default)
    {
        if (!State.BeginLoadMore())
        {
            return ChronicleLoadOutcome.Superseded;
        }

        ChronicleQuery query = State.Query! with { Cursor = State.NextCursor };
        int generation = ++requestGeneration;
        CancellationTokenSource source =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        activeRequest = source;

        try
        {
            ChroniclePage page = await queryService
                .QueryAsync(query, source.Token)
                .ConfigureAwait(false);
            if (generation != requestGeneration)
            {
                return ChronicleLoadOutcome.Superseded;
            }

            State.ApplyPage(page);
            return Outcome(page);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            if (generation == requestGeneration)
            {
                State.ApplyCancelled();
            }
            return ChronicleLoadOutcome.Superseded;
        }
        catch (Exception exception)
        {
            if (generation == requestGeneration)
            {
                State.ApplyTransportError(exception.Message);
            }
            return ChronicleLoadOutcome.Failed;
        }
        finally
        {
            Release(source);
        }
    }

    /// <summary>Cancel any in-flight request without discarding loaded entries.</summary>
    public void CancelActive()
    {
        CancellationTokenSource? current = activeRequest;
        if (current is null)
        {
            return;
        }
        try
        {
            current.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Reset()
    {
        CancelActive();
        requestGeneration++;
        State.Reset();
    }

    private ChronicleLoadOutcome Outcome(ChroniclePage page)
    {
        if (page.Error != ChronicleQueryError.None)
        {
            return State.RequiresReload
                ? ChronicleLoadOutcome.Stale
                : ChronicleLoadOutcome.Failed;
        }
        return State.LoadState == ChronicleLoadState.Empty
            ? ChronicleLoadOutcome.Empty
            : ChronicleLoadOutcome.Loaded;
    }

    private void CancelActiveRequest()
    {
        CancelActive();
    }

    private void Release(CancellationTokenSource source)
    {
        if (ReferenceEquals(activeRequest, source))
        {
            activeRequest = null;
        }
        source.Dispose();
    }
}
