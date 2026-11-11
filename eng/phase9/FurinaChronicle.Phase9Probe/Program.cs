// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Diagnostics;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Infrastructure.Passport;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Services.Passport;
using QRCoder;

if (args.Length is < 1 or > 2 ||
    (args.Length == 2 && args[1] is not ("current" or "previous")) ||
    !GameUidValidation.IsStructurallyValidUid(args[0]) ||
    GameServerRegionResolver.Resolve(args[0]) is not (
        GameServerRegion.ChinaOfficial or GameServerRegion.ChinaBilibili))
{
    Console.Error.WriteLine("Usage: FurinaChronicle.Phase9Probe <mainland-game-uid> [current|previous]");
    return 2;
}

string uid = args[0];
SpiralAbyssPeriod period = args.Length == 2 && args[1] == "previous"
    ? SpiralAbyssPeriod.Previous : SpiralAbyssPeriod.Current;

using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};
CancellationToken ct = cancellation.Token;
DirectoryInfo directory = Directory.CreateTempSubdirectory("furina-phase9-");
string qrPath = Path.Combine(directory.FullName, "sign-in.png");
string step = "QR creation";
try
{
    using var passportClient = new MiHoYoPassportClient();
    PassportDeviceIdentity device = PassportDeviceIdentity.Create();
    PassportQrSession session = await passportClient.CreateQrSessionAsync(device, ct);
    using var generator = new QRCodeGenerator();
    using QRCodeData qrData = generator.CreateQrCode(session.Url, QRCodeGenerator.ECCLevel.M);
    await File.WriteAllBytesAsync(qrPath, new PngByteQRCode(qrData).GetGraphic(8), ct);

    if (OperatingSystem.IsWindows())
    {
        try
        {
            using Process? viewer = Process.Start(new ProcessStartInfo(qrPath) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // The user can still open the displayed file path.
        }
    }
    Console.WriteLine($"Scan in MiYouShe and approve: {qrPath}");

    step = "QR confirmation";
    PassportLoginTokens tokens;
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        PassportQrPollResult poll = await passportClient.PollQrSessionAsync(session, ct);
        if (poll.Status == PassportQrStatus.Expired)
        {
            Console.Error.WriteLine("QR session expired.");
            return 3;
        }
        if (poll is { Status: PassportQrStatus.Confirmed, Tokens: { } confirmed })
        {
            tokens = confirmed;
            break;
        }
    }

    step = "session credentials";
    // A fingerprint issued by the login endpoint belongs to the same device
    // context as the session; dropping it makes the probe diverge from the
    // application and from the record requests it is meant to diagnose.
    device = device with { DeviceFingerprint = tokens.DeviceFingerprint };
    var clock = TimeProvider.System;
    DateTimeOffset now = clock.GetUtcNow();
    var credentials = new PassportCredentials(
        tokens.SToken, tokens.LToken, tokens.CookieToken, now, now, now, now);
    var passport = new PassportAccount(Guid.NewGuid(), tokens.Aid, tokens.Mid, null,
        PassportLoginMethod.QrCode, credentials, device, now, now);
    if (passport.Credentials.LToken is null || passport.Credentials.CookieToken is null)
    {
        PassportDerivedTokens derived = await passportClient.GetDerivedTokensAsync(passport, ct);
        passport = passport.WithCredentials(
            credentials.WithDerivedTokens(derived.LToken, derived.CookieToken, clock.GetUtcNow()),
            clock.GetUtcNow());
    }

    step = "isolated archive";
    var archive = new PlayerArchive(Guid.NewGuid(), "Phase 9 live probe", now, now);
    GameServerRegion region = GameServerRegionResolver.Resolve(uid);
    var target = new GameAccount(Guid.NewGuid(), archive.Id, uid, region, null, false,
        now, now, GenshinGameRoleIdentity.CreateIdentity(uid, region));

    step = "role binding and collection";
    using var profile = new MiHoYoAccountProfileClient();
    using var source = new HoYoGameRecordSpiralAbyssSource(clock);
    SpiralAbyssValidationResult result = await new ValidateSpiralAbyssCollection(
        profile, source, clock).ExecuteAsync(target, passport, period, ct);
    var first = result.First;
    Console.WriteLine($"First request: {first.Status}, HTTP: {first.HttpStatus}, service code: {first.ProviderCode}, commit: {first.Commit?.Status}");
    if (first.Commit?.Status != ChangeExecutionStatus.Applied)
    {
        return 4;
    }

    var second = result.Repeated!;
    Console.WriteLine($"Second request: {second.Status}, HTTP: {second.HttpStatus}, service code: {second.ProviderCode}, commit: {second.Commit?.Status}");
    if (second.Commit?.Status != ChangeExecutionStatus.NoOp)
    {
        return 5;
    }

    var observations = result.ReopenedObservations;
    if (observations.Count != 1)
    {
        Console.Error.WriteLine($"Unexpected observation count after reopen: {observations.Count}.");
        return 6;
    }
    var observation = observations[0];
    Console.WriteLine($"Reopened: season={observation.SeasonId} " +
        $"floors={observation.SpiralAbyssFloors.Count} " +
        $"chambers={observation.SpiralAbyssFloors.Sum(floor => floor.Chambers.Count)}");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine($"{step}: canceled or timed out.");
    return 7;
}
catch (Exception exception)
{
    // Server error bodies and request metadata can contain private values.
    Console.Error.WriteLine($"{step}: {exception.GetType().Name}");
    return 8;
}
finally
{
    try
    {
        File.Delete(qrPath);
        directory.Delete();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Could not remove temporary files from {directory.FullName}.");
    }
}
