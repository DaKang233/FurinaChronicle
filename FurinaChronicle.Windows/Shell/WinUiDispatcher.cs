// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using Microsoft.UI.Dispatching;

namespace FurinaChronicle.Windows.Shell;

// Single UI-dispatch port shared by all page state coordinators. Introduced in UI-4 so gacha and
// management pages no longer each define their own dispatcher.
public sealed class WinUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        if (queue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }))
        {
            completion.TrySetCanceled();
        }

        return completion.Task;
    }
}
