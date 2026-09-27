using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Services.Passport;

public sealed class MobileCaptchaCooldownTests
{
    [Fact]
    public void Start_BlocksForSixtySeconds()
    {
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero));
        var cooldown = new MobileCaptchaCooldown(
            timeProvider,
            TimeSpan.FromSeconds(60));

        cooldown.Start();

        Assert.True(cooldown.IsActive);
        Assert.Equal(60, cooldown.RemainingSeconds);
        timeProvider.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(1, cooldown.RemainingSeconds);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.False(cooldown.IsActive);
        Assert.Equal(0, cooldown.RemainingSeconds);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }
}
