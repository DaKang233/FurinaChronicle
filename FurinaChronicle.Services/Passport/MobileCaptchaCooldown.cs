namespace FurinaChronicle.Services.Passport;

public sealed class MobileCaptchaCooldown(
    TimeProvider timeProvider,
    TimeSpan duration)
{
    public MobileCaptchaCooldown()
        : this(TimeProvider.System, TimeSpan.FromSeconds(60))
    {
    }

    private DateTimeOffset availableAt;

    public int RemainingSeconds
    {
        get
        {
            TimeSpan remaining = availableAt - timeProvider.GetUtcNow();
            return remaining <= TimeSpan.Zero
                ? 0
                : (int)Math.Ceiling(remaining.TotalSeconds);
        }
    }

    public bool IsActive => RemainingSeconds > 0;

    public void Start()
    {
        availableAt = timeProvider.GetUtcNow() + duration;
    }
}
