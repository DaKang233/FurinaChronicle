// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Analytics;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Windows.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;

namespace FurinaChronicle.Windows.Composition;

// The single composition root for the isolated WinUI host. Registrations are intentionally local
// to FurinaChronicle.Windows for now; the platform-agnostic subset can be extracted into a shared
// composition project once the MAUI composition no longer carries platform dependencies.
public sealed class WindowsComposition : IAsyncDisposable
{
    private readonly ServiceProvider provider;

    private WindowsComposition(
        ServiceProvider provider,
        WindowsPaths paths,
        Ui1PreviewEnvironment environment,
        WindowsVersionInfo version)
    {
        this.provider = provider;
        Paths = paths;
        Environment = environment;
        Version = version;
    }

    public WindowsPaths Paths { get; }

    public Ui1PreviewEnvironment Environment { get; }

    public WindowsVersionInfo Version { get; }

    public IServiceProvider Services => provider;

    public static WindowsComposition Create()
    {
        var paths = new WindowsPaths();
        var environment = new Ui1PreviewEnvironment(paths.BaseDirectory, "WinUI");
        var version = WindowsVersionInfo.FromAssembly(typeof(WindowsComposition).Assembly);
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The WinUI composition root must run on the UI thread.");
        var dispatcher = new WinUiDispatcher(dispatcherQueue);
        var filePicker = new WindowsFilePickerService();
        var selectionStore = new JsonFileArchiveSelectionStore(paths.SelectionFilePath);

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton(environment);
        services.AddSingleton(version);
        services.AddSingleton<IUiDispatcher>(dispatcher);
        services.AddSingleton<IFilePickerService>(filePicker);
        services.AddSingleton<IArchiveSelectionStore>(selectionStore);

        services.AddSingleton(environment.Archives);
        services.AddSingleton<IPlayerArchiveRepository>(environment.Archives);
        services.AddSingleton(environment.Accounts);
        services.AddSingleton<IGameAccountRepository>(environment.Accounts);
        services.AddSingleton(environment.Records);
        services.AddSingleton<IGachaRecordRepository>(environment.Records);
        services.AddSingleton(environment.Changes);
        services.AddSingleton<IGachaAtomicChangeStore>(environment.Changes);
        services.AddSingleton<IArchiveOperationHistoryStore>(environment.Changes);
        services.AddSingleton<IArchiveDomainHistoryStore>(environment.Changes);

        services.AddSingleton<GetPlayerArchives>();
        services.AddSingleton<GetGameAccounts>();
        services.AddSingleton<GetUnresolvedGameAccounts>();
        services.AddSingleton<GetGachaRecordPage>();
        services.AddSingleton<GetRecentGachaRecords>();
        services.AddSingleton(sp => new CreatePlayerArchive(sp.GetRequiredService<IPlayerArchiveRepository>()));
        services.AddSingleton(sp => new RenamePlayerArchive(sp.GetRequiredService<IPlayerArchiveRepository>()));
        services.AddSingleton<DeletePlayerArchive>();
        services.AddSingleton<AddGameAccount>();
        services.AddSingleton<UpdateGameAccount>();
        services.AddSingleton<DeleteGameAccount>();
        services.AddSingleton<ArchiveSelectionService>();
        services.AddSingleton<ManageGachaHistory>();
        // Metadata/icon cache are part of the shared environment; registered for pages that need
        // them later. They are externally owned and disposed by Ui1PreviewEnvironment.
        services.AddSingleton<IGachaItemIconCache>(environment.GachaIconCache);
        services.AddSingleton<IGachaItemMetadataProvider>(new EmptyGachaItemMetadataProvider());

        services.AddSingleton<IArchiveManagementSource, ServiceArchiveManagementSource>();
        services.AddTransient<ArchiveManagementState>();
        services.AddSingleton<IOverviewSource, ServiceOverviewSource>();
        services.AddTransient<OverviewState>();
        services.AddSingleton<IStartupProbe, WindowsStartupProbe>();
        services.AddTransient<StartupState>();

        services.AddSingleton(sp => BuildGachaPageServices(
            environment,
            dispatcher,
            filePicker,
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(TimeProvider.System);

        return new WindowsComposition(
            services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = false,
                ValidateScopes = false
            }),
            paths,
            environment,
            version);
    }

    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        // The environment instance is externally owned by this composition root.
        await Environment.DisposeAsync();
    }

    private static GachaPageServices BuildGachaPageServices(
        Ui1PreviewEnvironment environment,
        IUiDispatcher dispatcher,
        IFilePickerService filePicker,
        TimeProvider timeProvider)
    {
        var planning = new ServiceGachaImportPlanningSource(
            new GetPlayerArchives(environment.Archives),
            new GetGameAccounts(environment.Archives, environment.Accounts));
        var readOnlySource = new ServiceGachaReadOnlySource(
            new GetPlayerArchives(environment.Archives),
            new GetGameAccounts(environment.Archives, environment.Accounts),
            new GetGachaRecordPage(environment.Records),
            environment.InitializeAsync,
            environment.GetCachedIconPath,
            new BuildGachaAnalytics(
                environment.Records,
                new EmptyGachaItemMetadataProvider(),
                new EmbeddedGachaEventCatalog()),
            environment.Changes.GetRecordRevisionsAsync);
        return new GachaPageServices(
            environment,
            readOnlySource,
            dispatcher,
            filePicker,
            new GachaImportCoordinator(
                environment.GachaImportReader,
                new ServiceGachaUigfImporter(environment.GachaImporter),
                planning),
            new GachaPortableImportCoordinator(
                environment.PortableInputStager,
                environment.PortableIndexFactory,
                new ServiceGachaPortableImportPlanner(environment.PortablePlanner),
                environment.PortableApplier,
                timeProvider),
            new GachaPortableExportPlanner(planning),
            new GachaRecordOperationCoordinator(environment.GachaHistory, environment.Changes),
            new GachaManualRefreshCoordinator(
                planning,
                new ServiceGachaManualRefreshGateway(environment.GachaRefresher)),
            new GachaWindowsCacheRefreshCoordinator(
                planning,
                new ServiceGachaWindowsCacheRefreshGateway(
                    environment.GachaRefresher,
                    environment.WindowsCacheUrlProvider)),
            new GachaTeyvatHelperImportCoordinator(
                planning,
                new ServiceGachaTeyvatHelperImportGateway(environment.TeyvatHelperImport)));
    }
}
