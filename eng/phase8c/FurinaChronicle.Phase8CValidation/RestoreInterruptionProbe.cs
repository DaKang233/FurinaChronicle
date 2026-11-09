// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Text.Json;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.History;

internal static class RestoreInterruptionProbe
{
    private const string ExtraRecordId = "9999999999999999999";
    private const string OwnerFile = "phase8c7-probe";

    public static async Task RunAsync(
        string command, string parent, string runId, string platform,
        Action<string> report)
    {
        if (!Path.IsPathFullyQualified(parent) ||
            !Guid.TryParseExact(runId, "N", out _))
        {
            throw new ArgumentException("Provide an absolute parent directory and an N-format run GUID.");
        }

        string root = Path.Combine(parent, $"phase8c7-{runId}");
        string databasePath = Path.Combine(root, "probe.db3");
        string packagePath = Path.Combine(root, "source.fca");
        string planPath = Path.Combine(root, "plan.json");
        string receiptPath = Path.Combine(root, "committed.json");
        string marker = Path.Combine(root, OwnerFile);
        void Report(string message) =>
            report($"run={runId} pid={Environment.ProcessId} {message}");

        if (command == "prepare")
        {
            Require(!Path.Exists(root), "The probe directory must be new.");
            Directory.CreateDirectory(root);
            await using (var owner = new FileStream(marker, FileMode.CreateNew))
            {
                await JsonSerializer.SerializeAsync(owner, runId);
            }
            await using var fixture = SyntheticArchiveSnapshot.Create(platform);
            await using var output = new FileStream(packagePath, FileMode.CreateNew);
            await new ArchivePackageWriter().WriteAsync(
                output, fixture,
                new ArchiveGeneratorInfo("Phase 8C restore probe", "0.8.16", "8C.7",
                    "synthetic", platform));
        }
        else
        {
            Require(File.Exists(marker) && File.Exists(databasePath) &&
                JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(marker)) == runId,
                "An existing owned probe database is required.");
        }

        var options = new SqliteDatabaseOptions(databasePath);
        var capacity = new DatabaseStorageCapacityProvider(options);
        await using var database = new FurinaDatabase(options);
        await database.InitializeAsync();
        var snapshots = new SqliteArchiveRestoreTargetSnapshotFactory(
            new SqliteArchiveExportSnapshotFactory(database, TimeProvider.System,
                new ArchiveSnapshotStorageOptions(Path.Combine(root, "snapshots"))));
        var stager = new SqliteArchiveRestoreInputStager(new ArchivePackageReader(),
            new ArchiveRestoreStorageOptions(Path.Combine(root, "staging")));
        await using var input = File.OpenRead(packagePath);
        await using IArchiveRestoreStagedPackage package = await stager.StageAsync(input);

        var store = new SqliteGachaAtomicChangeStore(database, capacity);
        if (command == "prepare")
        {
            ProbePlan prepared = await PrepareAsync(database, store, snapshots, package);
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(prepared));
            Report($"PREPARED operation={prepared.OperationId:D} " +
                $"before={prepared.Plan.TargetStateToken} package={package.PackageFingerprint}");
            return;
        }

        ProbePlan saved = JsonSerializer.Deserialize<ProbePlan>(
            await File.ReadAllTextAsync(planPath)) ??
            throw new InvalidDataException("The saved probe plan is missing.");
        var request = Request(package, saved.Plan, new OperationId(saved.OperationId));
        var extra = new GachaFactReference(saved.TargetAccountId, ExtraRecordId);
        switch (command)
        {
            case "before":
                var interrupted = new SqliteGachaAtomicChangeStore(database,
                    capacity, new BeforeCommitBarrier(Report));
                await interrupted.ApplyAsync(request);
                throw new InvalidOperationException("The before-commit barrier was not reached.");
            case "recover":
                Require(await StateTokenAsync(snapshots) == saved.Plan.TargetStateToken,
                    "Reopen did not restore the original database state.");
                Require(await store.GetCurrentAsync(extra) is not null,
                    "The target-only record was lost before commit.");
                Require(await store.FindReceiptAsync(request.OperationId) is null,
                    "An uncommitted receipt survived.");
                Require(await store.FindCurrentRecoveryPointAsync() is null,
                    "An uncommitted recovery-point row survived.");
                int removed = await store.CleanupOrphanedRecoveryPointsAsync();
                Require(removed == 1, "Expected one orphaned recovery-point file.");
                await CheckIntegrityAsync(database);
                Report($"ROLLED_BACK token={saved.Plan.TargetStateToken} receipt=absent orphan_removed={removed}");
                break;
            case "after":
                ArchiveRestoreApplyResult applied = await store.ApplyAsync(request);
                Require(applied.Status == ChangeExecutionStatus.Applied && applied.Receipt is not null,
                    $"Expected an applied Replace, got {applied.Status}: {applied.ConflictReason}");
                ArchiveRestoreReceipt receipt = applied.Receipt!;
                var checkpoint = new CommittedCheckpoint(receipt.ReceiptId,
                    await StateTokenAsync(snapshots));
                await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(checkpoint));
                Pause(Report, $"AFTER_COMMIT operation={request.OperationId} receipt={receipt.ReceiptId:D}");
                break;
            case "check":
                CommittedCheckpoint committed = JsonSerializer.Deserialize<CommittedCheckpoint>(
                    await File.ReadAllTextAsync(receiptPath)) ??
                    throw new InvalidDataException("The commit checkpoint is missing.");
                ArchiveRestoreReceipt restored = await store.FindReceiptAsync(request.OperationId) ??
                    throw new InvalidDataException("The original committed receipt was not found.");
                Require(restored.ReceiptId == committed.ReceiptId &&
                    restored.PackageFingerprint == package.PackageFingerprint &&
                    restored.PlanFingerprint == saved.Plan.PlanFingerprint &&
                    restored.Mode == ArchiveRestoreMode.Replace &&
                    restored.Status == ChangeExecutionStatus.Applied &&
                    restored.Archives.Select(item => (item.SourceArchiveReference, (Guid?)item.TargetArchiveId))
                        .ToHashSet().SetEquals(saved.Plan.Archives.Select(item =>
                            (item.SourceArchiveReference, item.TargetArchiveId))) &&
                    restored.Accounts.Select(item =>
                        (item.SourceArchiveReference, item.SourceAccountReference,
                         (Guid?)item.TargetArchiveId, (Guid?)item.TargetAccountId, item.TargetRoleIdentityId))
                        .ToHashSet().SetEquals(saved.Plan.Accounts.Select(item =>
                            (item.SourceArchiveReference, item.SourceAccountReference,
                             item.TargetArchiveId, item.TargetAccountId, item.TargetRoleIdentityId))),
                    "The persisted receipt does not match the committed operation and mappings.");
                Require(await store.GetCurrentAsync(extra) is null,
                    "The committed replacement did not remove the target-only record.");
                ArchiveReplaceRecoveryPoint? point = await store.FindCurrentRecoveryPointAsync();
                Require(point is { IsValid: true } && point.RecoveryPointId == restored.RecoveryPointId,
                    "The committed recovery point is missing or invalid.");
                Require(await StateTokenAsync(snapshots) == committed.StateToken,
                    "Committed database contents changed on reopen.");
                ArchiveRestoreApplyResult replay = await store.ApplyAsync(request);
                Require(replay.Status == ChangeExecutionStatus.AlreadyCommitted &&
                    replay.Receipt?.ReceiptId == restored.ReceiptId &&
                    await StateTokenAsync(snapshots) == committed.StateToken,
                    "Replaying the same operation changed the database or receipt.");
                await CheckIntegrityAsync(database);
                Report($"REOPENED operation={request.OperationId} receipt={restored.ReceiptId:D} " +
                    $"package={restored.PackageFingerprint} plan={restored.PlanFingerprint} " +
                    $"archives={restored.Archives.Count} accounts={restored.Accounts.Count} " +
                    $"recovery={point!.RecoveryPointId:D} token={committed.StateToken} replay=AlreadyCommitted");
                break;
            default:
                throw new ArgumentException("Use prepare, before, recover, after or check.");
        }
    }

    private static async Task<ProbePlan> PrepareAsync(
        FurinaDatabase database, SqliteGachaAtomicChangeStore store,
        IArchiveRestoreTargetSnapshotFactory snapshots, IArchiveRestoreStagedPackage package)
    {
        var planner = new PlanArchiveRestore(snapshots, TimeProvider.System);
        ArchiveRestorePlan merge = await ConfirmAsync(planner, new ArchiveRestorePlanRequest(package));
        ArchiveRestoreApplyResult seeded = await store.ApplyAsync(Request(package, merge, OperationId.New()));
        ArchiveRestoreReceipt seed = seeded.Receipt ??
            throw new InvalidOperationException($"The synthetic seed failed: {seeded.ConflictReason}");
        var main = package.Catalog.Archives[0];
        Guid accountReference = main.Accounts[0].AccountReference;
        Guid targetAccount = seed.Accounts.Single(item => item.SourceAccountReference == accountReference)
            .TargetAccountId;
        var extra = new GachaRecord(targetAccount, ExtraRecordId, "Phase 8C.7 target only", 3,
            new DateTimeOffset(2026, 10, 28, 1, 0, 0, TimeSpan.Zero))
        {
            ItemId = "11301",
            ItemType = "weapon",
            GachaType = "301",
            UigfGachaType = "301"
        };
        await new SqliteGachaRecordRepository(database).SaveBatchAsync([extra]);
        var replace = new ArchiveRestorePlanRequest(package,
            seed.Archives.Select(item => new ArchiveRestoreArchiveDecision(item.SourceArchiveReference,
                ArchiveRestoreArchiveDecisionKind.MapExisting, item.TargetArchiveId)).ToArray(),
            seed.Accounts.Select(item => new ArchiveRestoreAccountDecision(item.SourceAccountReference,
                ArchiveRestoreAccountDecisionKind.MapExisting, item.TargetAccountId)).ToArray(),
            Mode: ArchiveRestoreMode.Replace,
            ReplaceSelections: [new ArchiveReplaceSelection(main.ArchiveReference,
                ArchiveReplaceScopeKind.Domain, accountReference, ArchiveReplaceDomain.Gacha)]);
        ArchiveRestorePlan plan = await ConfirmAsync(planner, replace);
        Require(plan.Removals.Count == 1, "The Replace probe must remove exactly one target-only fact.");
        return new ProbePlan(Guid.NewGuid(), targetAccount, plan);
    }

    private static async Task<ArchiveRestorePlan> ConfirmAsync(
        IArchiveRestorePlanner planner, ArchiveRestorePlanRequest request)
    {
        ArchiveRestorePlan preview = await planner.PlanAsync(request);
        ArchiveRestorePlan plan = await planner.PlanAsync(request with
        {
            Confirmations = preview.Confirmations.Where(item => !item.Satisfied)
                .Select(item => new ArchiveRestoreConfirmation(item.Kind, item.Reference,
                    item.MarkerKey, item.MarkerVersion)).ToArray()
        });
        Require(plan.CanApply, "The synthetic plan is blocked: " +
            string.Join("; ", plan.Conflicts.Select(item => item.Message)));
        return plan;
    }

    private static ArchiveRestoreApplyRequest Request(
        IArchiveRestoreStagedPackage package, ArchiveRestorePlan plan, OperationId operation)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ArchiveRestoreApplyRequest(package, plan, operation, now, now,
            "Phase 8C.7 synthetic interruption probe");
    }

    private static async Task<string> StateTokenAsync(IArchiveRestoreTargetSnapshotFactory snapshots)
    {
        await using IArchiveRestoreTargetSnapshot snapshot = await snapshots.OpenAsync();
        return snapshot.StateToken;
    }

    private static async Task CheckIntegrityAsync(FurinaDatabase database)
    {
        Require(await database.Connection.ExecuteScalarAsync<string>("PRAGMA quick_check;") == "ok" &&
            await database.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_foreign_key_check;") == 0,
            "The reopened database failed integrity checks.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private static void Pause(Action<string> report, string marker)
    {
        report(marker);
        // The external driver terminates this process at the observed boundary.
        Thread.Sleep(Timeout.Infinite);
        throw new UnreachableException();
    }

    private sealed class BeforeCommitBarrier(Action<string> report) : IArchiveRestoreFailureInjector
    {
        public void ThrowIfRequested(ArchiveRestoreFailurePoint point)
        {
            if (point == ArchiveRestoreFailurePoint.BeforeCommit)
            {
                Pause(report, "BEFORE_COMMIT writes=complete receipt_inserted=true");
            }
        }
    }

    private sealed record ProbePlan(Guid OperationId, Guid TargetAccountId, ArchiveRestorePlan Plan);
    private sealed record CommittedCheckpoint(Guid ReceiptId, string StateToken);
}
