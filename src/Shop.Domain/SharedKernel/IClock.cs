namespace Shop.Domain.SharedKernel;

/// <summary>Time port so all timestamps are UTC and deterministic in tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
