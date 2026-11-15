// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Chronicle;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Tests.Presentation;

// UI-5 wiring coverage: the same formal services registered by WindowsComposition must load the
// three page states against the isolated synthetic environment. Chronicle has gacha seed data;
// character/weapon and challenge histories are intentionally empty in the seed.
public sealed class Ui5WiringIntegrationTests
{
    [Fact]
    public async Task ThreePageStatesResolveAgainstIsolatedEnvironment()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleUi5Wiring",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            var getArchives = new GetPlayerArchives(environment.Archives);
            var getAccounts = new GetGameAccounts(environment.Archives, environment.Accounts);
            var chronicleQuery = new SqliteChronicleQueryService(
                environment.Database,
                environment.Records,
                environment.Accounts,
                environment.Changes);

            var chronicle = new ChronicleWorkspaceState(
                chronicleQuery, getArchives, getAccounts);
            await chronicle.EnterAsync();
            Assert.False(string.IsNullOrWhiteSpace(chronicle.Snapshot.GenerationId));
            Assert.Null(chronicle.Snapshot.Error);
            Assert.True(chronicle.Snapshot.Archives.Count >= 1);
            Assert.NotEmpty(chronicle.Snapshot.Entries);
            chronicle.Cancel();

            var characters = new CharacterWeaponHistoryState(
                environment.Changes, getArchives, getAccounts);
            await characters.EnterAsync();
            Assert.Null(characters.Snapshot.Error);
            Assert.Empty(characters.Snapshot.Observations);
            characters.Cancel();

            var challenges = new ChallengeHistoryState(
                environment.Changes, getArchives, getAccounts);
            await challenges.EnterAsync();
            Assert.Null(challenges.Snapshot.Error);
            Assert.Empty(challenges.Snapshot.Observations);
            challenges.Cancel();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
