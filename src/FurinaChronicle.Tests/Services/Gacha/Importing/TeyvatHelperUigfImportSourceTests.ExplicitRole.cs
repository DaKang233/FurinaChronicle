// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Tests.Services.Gacha.Importing;

public sealed partial class TeyvatHelperUigfImportSourceTests
{
    [Fact]
    public async Task ExplicitRoleGeneratesFreshUrlsWithoutChangingAnotherPagesSelection()
    {
        var account = Passport();
        var selection = new StubSelectionStore(new(account.Id, "123456788"));
        var provider = new StubSTokenProvider();
        var client = new RecordingClient();
        var source = new TeyvatHelperUigfImportSource(new StubAccountStore(account), selection, provider, client);
        await source.DownloadForRoleAsync(account.Id, "123456789");
        await source.DownloadForRoleAsync(account.Id, "123456789");
        Assert.Equal("123456788", (await selection.LoadAsync())!.GameUid);
        Assert.Equal(2, provider.CallCount);
        Assert.All(client.Requests, request => Assert.Equal("123456789", request.Uid));
        Assert.NotEqual(client.Requests[0].Url, client.Requests[1].Url);
    }

    [Fact]
    public async Task ExplicitRoleDoesNotNeedAGlobalSelectionStoreAndRejectsDeletedCredentials()
    {
        var account = Passport();
        var store = new StubAccountStore(account);
        var provider = new StubSTokenProvider();
        var client = new RecordingClient();
        var source = new TeyvatHelperUigfImportSource(store, null, provider, client);
        Assert.False((await source.GetAvailabilityAsync()).CanAutomaticallyImport);
        await source.DownloadForRoleAsync(account.Id, "123456789");
        await store.DeleteAsync(account.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.DownloadForRoleAsync(account.Id, "123456789"));
        Assert.Equal(1, provider.CallCount);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task ExplicitOverseasRoleIsRejectedBeforeGeneratingOrSendingALink()
    {
        var account = Passport(PassportRealm.Oversea);
        var provider = new StubSTokenProvider();
        var client = new RecordingClient();
        var source = new TeyvatHelperUigfImportSource(new StubAccountStore(account), null, provider, client);
        await Assert.ThrowsAsync<NotSupportedException>(() => source.DownloadForRoleAsync(account.Id, "812345678"));
        Assert.Equal(0, provider.CallCount);
        Assert.Empty(client.Requests);
    }
}
