// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Presentation.Gacha;

// Ordered UI delivery; a broken or closed display must not abort a network fetch or a commit.
public sealed class GachaRefreshProgressReporter(
    IUiDispatcher dispatcher, Action<GachaRefreshProgress> apply) : IProgress<GachaRefreshProgress>, IDisposable
{
    private readonly object gate = new();
    private Task pending = Task.CompletedTask;
    private volatile bool closed;

    public void Report(GachaRefreshProgress value)
    {
        lock (gate)
        {
            if (closed) return;
            var snapshot = value with { Records = value.Records.ToArray() };
            pending = DeliverAsync(pending, snapshot);
        }
    }

    public Task DrainAsync()
    {
        lock (gate) return pending;
    }

    public void Dispose() => closed = true;

    private async Task DeliverAsync(Task previous, GachaRefreshProgress value)
    {
        try
        {
            await previous.ConfigureAwait(false);
            if (!closed)
                await dispatcher.InvokeAsync(() => { if (!closed) apply(value); }).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Progress is optional. Do not change the outcome of the domain operation.
        }
    }
}
