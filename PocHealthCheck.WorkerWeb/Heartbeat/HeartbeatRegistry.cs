using System.Collections.Concurrent;

namespace PocHealthCheck.WorkerWeb.Heartbeat;

public sealed record HeartbeatEntry(
    string ComponentName,
    DateTimeOffset LastBeatUtc,
    long SequenceNumber,
    string StatusMessage,
    TimeSpan DegradedAfter,
    TimeSpan UnhealthyAfter)
{
    public TimeSpan ElapsedSinceLastBeat(DateTimeOffset nowUtc) =>
        nowUtc > LastBeatUtc ? nowUtc - LastBeatUtc : TimeSpan.Zero;
}

public sealed class HeartbeatRegistry
{
    public const string ServiceComponentName = "WorkerWebHost";

    private readonly ConcurrentDictionary<string, HeartbeatEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset ServiceStartedAtUtc { get; } = DateTimeOffset.UtcNow;

    public void Register(
        string componentName,
        TimeSpan degradedAfter,
        TimeSpan unhealthyAfter,
        string initialMessage = "Zarejestrowano komponent, oczekiwanie na pierwszy puls.")
    {
        var now = DateTimeOffset.UtcNow;
        _entries[componentName] = new HeartbeatEntry(
            componentName,
            now,
            SequenceNumber: 0,
            initialMessage,
            degradedAfter,
            unhealthyAfter);
    }

    public void Beat(string componentName, string statusMessage = "OK")
    {
        var now = DateTimeOffset.UtcNow;
        _entries.AddOrUpdate(
            componentName,
            name => new HeartbeatEntry(
                name,
                now,
                SequenceNumber: 1,
                statusMessage,
                DegradedAfter: TimeSpan.FromSeconds(12),
                UnhealthyAfter: TimeSpan.FromSeconds(22)),
            (_, existing) => existing with
            {
                LastBeatUtc = now,
                SequenceNumber = existing.SequenceNumber + 1,
                StatusMessage = statusMessage
            });
    }

    public HeartbeatEntry? Get(string componentName) =>
        _entries.TryGetValue(componentName, out var entry) ? entry : null;

    public IReadOnlyList<HeartbeatEntry> GetAll() =>
        _entries.Values
            .OrderBy(e => e.ComponentName, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
