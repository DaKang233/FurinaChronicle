// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;

namespace FurinaChronicle.Windows.Composition;

// Lazily owns the one host environment (production or isolated). Construction is deferred until
// the startup gate has proven the actual data root usable, so a bad path can never throw from the
// application launch path before the error UI exists.
public sealed class EnvironmentHolder(WindowsPaths paths)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private WindowsHostEnvironment? environment;

    public WindowsHostEnvironment? Current => environment;

    public async Task<WindowsHostEnvironment> GetOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return environment ??= new WindowsHostEnvironment(paths);
        }
        finally
        {
            gate.Release();
        }
    }

    public WindowsHostEnvironment GetRequired() =>
        environment ?? throw new InvalidOperationException(
            "The host environment is not ready; the startup gate must pass first.");
}
