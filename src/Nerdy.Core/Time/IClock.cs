namespace Nerdy.Core.Time;

/// <summary>Abstracts "now" so planning, stats and timers are testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateTime LocalNow => UtcNow.ToLocalTime().DateTime;
    DateOnly Today => DateOnly.FromDateTime(LocalNow);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
