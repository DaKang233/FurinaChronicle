// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;

namespace FurinaChronicle.Windows.Composition;

// Lazily owns the one isolated environment. Construction is deferred until the startup guard has
// proven the root directory writable, so a bad FURINA_UI1_DIRECTORY can never throw from the
// application launch path before the error UI exists.
public sealed class EnvironmentHolder(WindowsPaths paths)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Ui1PreviewEnvironment? environment;

    public Ui1PreviewEnvironment? Current => environment;

    public async Task<Ui1PreviewEnvironment> GetOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return environment ??= new Ui1PreviewEnvironment(paths.BaseDirectory, "WinUI");
        }
        finally
        {
            gate.Release();
        }
    }

    public Ui1PreviewEnvironment GetRequired() =>
        environment ?? throw new InvalidOperationException(
            "The isolated environment is not ready; the startup gate must pass first.");
}
