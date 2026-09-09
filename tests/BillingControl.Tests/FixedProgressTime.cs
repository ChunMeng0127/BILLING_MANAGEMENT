namespace BillingControl.Tests;

public sealed class FixedProgressTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 22, 4, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
