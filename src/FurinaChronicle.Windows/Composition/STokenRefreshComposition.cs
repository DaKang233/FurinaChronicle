// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Refreshing;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Passport;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Windows.Composition;

// Isolated-host SToken refresh composition. The real MiHoYo provider/client are constructed on the
// Windows side; WindowsHostEnvironment's UnavailableSTokenUrlProvider is untouched. Network calls are
// only made when the user explicitly starts a refresh with a locally stored credential.
public sealed class STokenRefreshComposition : IAsyncDisposable
{
    private readonly MiHoYoSTokenGachaUrlProvider provider;
    private readonly MiHoYoGachaLogClient client;

    public STokenRefreshComposition(
        WindowsHostEnvironment environment,
        IPassportAccountStore passportAccountStore,
        IGachaItemMetadataProvider? itemMetadata = null)
    {
        provider = new MiHoYoSTokenGachaUrlProvider();
        client = new MiHoYoGachaLogClient();
        IGachaLogClient refreshClient = client;
#if DEBUG
        if (environment.IsSynthetic) refreshClient = SyntheticGachaRefreshClient.TryCreate() ?? refreshClient;
#endif
        ISTokenGachaUrlProvider importProvider = provider;
        ITeyvatHelperUigfClient importClient = environment.TeyvatClient;
#if DEBUG
        if (SyntheticTeyvatHelperSource.TryCreate(environment) is { } scripted)
        {
            importProvider = scripted;
            importClient = scripted;
        }
#endif
        TeyvatImport = new TeyvatHelperUigfImportSource(passportAccountStore, null, importProvider, importClient);
        Refresher = new RefreshGachaRecords(
            environment.Records,
            new CommitGachaRecords(environment.Changes),
            passportAccountStore,
            provider,
            environment.WindowsCacheUrlProvider,
            refreshClient,
            itemMetadata ?? new EmptyGachaItemMetadataProvider());
    }

    public RefreshGachaRecords Refresher { get; }

    public TeyvatHelperUigfImportSource TeyvatImport { get; }

    public ValueTask DisposeAsync()
    {
        provider.Dispose();
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
