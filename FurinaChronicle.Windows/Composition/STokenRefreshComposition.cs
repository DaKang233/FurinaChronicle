// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Refreshing;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Windows.Composition;

// Isolated-host SToken refresh composition. The real MiHoYo provider/client are constructed on the
// Windows side; Ui1PreviewEnvironment's UnavailableSTokenUrlProvider is untouched. Network calls are
// only made when the user explicitly starts a refresh with a locally stored credential.
public sealed class STokenRefreshComposition : IAsyncDisposable
{
    private readonly MiHoYoSTokenGachaUrlProvider provider;
    private readonly MiHoYoGachaLogClient client;

    public STokenRefreshComposition(
        Ui1PreviewEnvironment environment,
        IPassportAccountStore passportAccountStore)
    {
        provider = new MiHoYoSTokenGachaUrlProvider();
        client = new MiHoYoGachaLogClient();
        Refresher = new RefreshGachaRecords(
            environment.Records,
            new CommitGachaRecords(environment.Changes),
            passportAccountStore,
            provider,
            environment.WindowsCacheUrlProvider,
            client,
            new EmptyGachaItemMetadataProvider());
    }

    public RefreshGachaRecords Refresher { get; }

    public ValueTask DisposeAsync()
    {
        provider.Dispose();
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
