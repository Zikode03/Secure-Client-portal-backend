namespace SecureClientPortal.Backend.Tests;
// The fixtures create August/September 2026 reporting periods. Freeze their clock
// so default effective dates are tested independently of the machine's date.
internal sealed class FixedProfileTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
}
