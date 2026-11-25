using System.Collections.Concurrent;

namespace PocHealthCheck.Api.Heartbeat;

public sealed record ComponentPulseDto(
    string ComponentName,
    DateTimeOffset LastBeatUtc,
    double AgeSeconds,
    long SequenceNumber,
    string StatusMessage);

public sealed record WorkerSummaryDto(
    string WorkerName,
    bool IsRunning,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastIterationCompletedAtUtc,
    long CompletedIterations,
    long TotalRetryCount,
    long FailedIterations,
    bool LastIterationSucceeded,
    string? LastErrorMessage,
    bool IsHeartbeatPaused,
    bool IsForcedFailureEnabled);

public sealed record IncomingHeartbeatDto(
    string ServiceName,
    string InstanceId,
    DateTimeOffset SentAtUtc,
    double UptimeSeconds,
    IReadOnlyList<ComponentPulseDto> Components,
    IReadOnlyList<WorkerSummaryDto> Workers);

public sealed record StoredServiceHeartbeat(
    string ServiceName,
    string InstanceId,
    DateTimeOffset ReceivedAtUtc,
    long ReceivedCount,
    IncomingHeartbeatDto LastPayload)
{
    public TimeSpan ElapsedSinceReceived(DateTimeOffset nowUtc) =>
        nowUtc > ReceivedAtUtc ? nowUtc - ReceivedAtUtc : TimeSpan.Zero;
}

public sealed class RemoteHeartbeatStore
{
    private readonly ConcurrentDictionary<string, StoredServiceHeartbeat> _heartbeats =
        new(StringComparer.OrdinalIgnoreCase);

    public StoredServiceHeartbeat Record(IncomingHeartbeatDto payload)
    {
        var now = DateTimeOffset.UtcNow;
        return _heartbeats.AddOrUpdate(
            payload.ServiceName,
            _ => new StoredServiceHeartbeat(
                payload.ServiceName,
                payload.InstanceId,
                now,
                ReceivedCount: 1,
                payload),
            (_, existing) => existing with
            {
                InstanceId = payload.InstanceId,
                ReceivedAtUtc = now,
                ReceivedCount = existing.ReceivedCount + 1,
                LastPayload = payload
            });
    }

    public IReadOnlyList<StoredServiceHeartbeat> GetAll() =>
        _heartbeats.Values
            .OrderBy(h => h.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
