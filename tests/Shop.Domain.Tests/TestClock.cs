using Shop.Domain.SharedKernel;

namespace Shop.Domain.Tests;

public sealed class TestClock(DateTimeOffset? start = null) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = start ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}
