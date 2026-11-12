// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Infrastructure.Chronicle;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Chronicle;
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
//
// Important: constructing this composition must not create the isolated database or touch the
// configured data directory. The environment is created lazily only after the startup guard has
// proven the root writable, so an invalid FURINA_UI1_DIRECTORY shows the startup failure page
// instead of throwing out of Application.OnLaunched.
public sealed class WindowsComposition : IAsyncDisposable
{
    private readonly ServiceProvider provider;

    private WindowsComposition(
        ServiceProvider provider,
        WindowsPaths paths,
        EnvironmentHolder environmentHolder,
        WindowsVersionInfo version)
    {
        this.provider = provider;
        Paths = paths;
        EnvironmentHolder = environmentHolder;
        Version = version;
    }

    public WindowsPaths Paths { get; }

    public EnvironmentHolder EnvironmentHolder { get; }

    public WindowsVersionInfo Version { get; }

    public IServiceProvider Services => provider;

    public static WindowsComposition Create()
    {
        var paths = new WindowsPaths();
        var environmentHolder = new EnvironmentHolder(paths);
        var version = WindowsVersionInfo.FromAssembly(typeof(WindowsComposition).Assembly);
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The WinUI composition root must run on the UI thread.");
        var dispatcher = new WinUiDispatcher(dispatcherQueue);
        var filePicker = new WindowsFilePickerService();
        var selectionStore = new JsonFileArchiveSelectionStore(paths.SelectionFilePath);

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton(environmentHolder);
        services.AddSingleton(version);
        services.AddSingleton<IUiDispatcher>(dispatcher);
        services.AddSingleton<IFilePickerService>(filePicker);
        services.AddSingleton<IArchiveSelectionStore>(selectionStore);

        // Environment and repositories are resolved lazily; nothing below is constructed until a
        // page or the startup probe asks for it after the path guard passed.
        services.AddSingleton(sp => sp.GetRequiredService<EnvironmentHolder>().GetRequired());
        services.AddSingleton(sp => sp.GetRequiredService<Ui1PreviewEnvironment>().Archives);
        services.AddSingleton(sp => sp.GetRequiredService<Ui1PreviewEnvironment>().Accounts);
        services.AddSingleton(sp => sp.GetRequiredService<Ui1PreviewEnvironment>().Records);
        services.AddSingleton(sp => sp.GetRequiredService<Ui1PreviewEnvironment>().Changes);
        services.AddSingleton<IPlayerArchiveRepository>(
            sp => sp.GetRequiredService<SqlitePlayerArchiveRepository>());
        services.AddSingleton<IGameAccountRepository>(
            sp => sp.GetRequiredService<SqliteGameAccountRepository>());
        services.AddSingleton<IGachaRecordRepository>(
            sp => sp.GetRequiredService<SqliteGachaRecordRepository>());
        services.AddSingleton<IGachaAtomicChangeStore>(
            sp => sp.GetRequiredService<SqliteGachaAtomicChangeStore>());
        services.AddSingleton<IArchiveOperationHistoryStore>(
            sp => sp.GetRequiredService<SqliteGachaAtomicChangeStore>());
        services.AddSingleton<IArchiveDomainHistoryStore>(
            sp => sp.GetRequiredService<SqliteGachaAtomicChangeStore>());

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

        services.AddSingleton<IArchiveManagementSource, ServiceArchiveManagementSource>();
        services.AddTransient<ArchiveManagementState>();
        services.AddSingleton<IOverviewSource, ServiceOverviewSource>();
        services.AddTransient<OverviewState>();
        services.AddSingleton<IArchiveHistorySource, ServiceArchiveHistorySource>();
        services.AddTransient<ArchiveHistoryState>();
        services.AddSingleton<ISettingsAppearanceStore, JsonAppearanceSettingsStore>();
        services.AddSingleton<ISettingsStoragePort, WindowsSettingsStoragePort>();
        services.AddSingleton<ISettingsHistoryPort, ServiceSettingsHistoryPort>();
        services.AddSingleton<ISettingsApplicationPort, WindowsSettingsApplicationPort>();
        services.AddTransient<SettingsState>();
        services.AddSingleton(sp => BuildArchiveTransferServiceSet(
            sp.GetRequiredService<Ui1PreviewEnvironment>()));
        services.AddSingleton<IArchiveTransferWorkspace>(
            sp => sp.GetRequiredService<ArchiveTransferServiceSet>().Workspace);
        services.AddSingleton<IArchiveTransferSource, ServiceArchiveTransferSource>();
        services.AddTransient<ArchiveTransferState>();

        // UI-5 pages: formal isolated Chronicle query service and page-local state machines.
        services.AddSingleton<IChronicleQueryService>(sp =>
        {
            Ui1PreviewEnvironment environment =
                sp.GetRequiredService<Ui1PreviewEnvironment>();
            return new SqliteChronicleQueryService(
                environment.Database,
                sp.GetRequiredService<IGachaRecordRepository>(),
                sp.GetRequiredService<IGameAccountRepository>(),
                sp.GetRequiredService<IArchiveDomainHistoryStore>());
        });
        services.AddTransient(sp => new ChronicleWorkspaceState(
            sp.GetRequiredService<IChronicleQueryService>(),
            new GetPlayerArchives(sp.GetRequiredService<IPlayerArchiveRepository>()),
            new GetGameAccounts(
                sp.GetRequiredService<IPlayerArchiveRepository>(),
                sp.GetRequiredService<IGameAccountRepository>())));
        services.AddTransient(sp => new CharacterWeaponHistoryState(
            sp.GetRequiredService<IArchiveDomainHistoryStore>(),
            new GetPlayerArchives(sp.GetRequiredService<IPlayerArchiveRepository>()),
            new GetGameAccounts(
                sp.GetRequiredService<IPlayerArchiveRepository>(),
                sp.GetRequiredService<IGameAccountRepository>())));
        services.AddTransient(sp => new ChallengeHistoryState(
            sp.GetRequiredService<IArchiveDomainHistoryStore>(),
            new GetPlayerArchives(sp.GetRequiredService<IPlayerArchiveRepository>()),
            new GetGameAccounts(
                sp.GetRequiredService<IPlayerArchiveRepository>(),
                sp.GetRequiredService<IGameAccountRepository>())));
        services.AddSingleton<IStartupProbe, WindowsStartupProbe>();
        services.AddTransient<StartupState>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => BuildGachaPageServices(
            sp.GetRequiredService<Ui1PreviewEnvironment>(),
            dispatcher,
            filePicker,
            sp.GetRequiredService<TimeProvider>()));

        return new WindowsComposition(
            services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = false,
                ValidateScopes = false
            }),
            paths,
            environmentHolder,
            version);
    }

    public async ValueTask DisposeAsync()
    {
        // The container owns the factory-created Ui1PreviewEnvironment and disposes it here.
        await provider.DisposeAsync();
    }

    private static ArchiveTransferServiceSet BuildArchiveTransferServiceSet(
        Ui1PreviewEnvironment environment)
    {
        var limits = new ArchivePortableLimits();
        var reader = new ArchivePackageReader(limits);
        var writer = new ArchivePackageWriter(limits);
        var snapshots = new SqliteArchiveExportSnapshotFactory(
            environment.Database,
            TimeProvider.System,
            new ArchiveSnapshotStorageOptions(
                Path.Combine(environment.TemporaryDirectory, "archive-export-snapshots")));
        var stager = new SqliteArchiveRestoreInputStager(
            reader,
            new ArchiveRestoreStorageOptions(
                Path.Combine(environment.TemporaryDirectory, "archive-restore-sessions")),
            limits);
        var targetSnapshots = new SqliteArchiveRestoreTargetSnapshotFactory(snapshots);
        var planner = new PlanArchiveRestore(targetSnapshots, TimeProvider.System);
        return new ArchiveTransferServiceSet(
            new ArchiveLogicalExporter(snapshots, writer, reader),
            stager,
            planner,
            environment.Changes,
            new WindowsArchiveTransferWorkspace(environment.TemporaryDirectory));
    }

    private static GachaPageServices BuildGachaPageServices(        Ui1PreviewEnvironment environment,
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
