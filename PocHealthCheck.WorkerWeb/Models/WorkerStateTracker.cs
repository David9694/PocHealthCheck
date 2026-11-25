using System.Collections.Concurrent;

namespace PocHealthCheck.WorkerWeb.Models;

public sealed class TransientWorkerException(string message) : Exception(message);

public sealed record WorkerSnapshot(
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

public sealed class WorkerStateTracker
{
    public const string OrderWorkerName = "OrderProcessingWorker";
    public const string ReportWorkerName = "ReportGenerationWorker";

    private sealed class MutableWorkerState(string workerName)
    {
        public string WorkerName { get; } = workerName;
        public bool IsRunning;
        public DateTimeOffset? StartedAtUtc;
        public DateTimeOffset? LastIterationCompletedAtUtc;
        public long CompletedIterations;
        public long TotalRetryCount;
        public long FailedIterations;
        public bool LastIterationSucceeded = true;
        public string? LastErrorMessage;
        public bool IsHeartbeatPaused;
        public bool IsForcedFailureEnabled;
        public readonly Lock SyncRoot = new();
    }

    private readonly ConcurrentDictionary<string, MutableWorkerState> _states = new(StringComparer.OrdinalIgnoreCase);

    public WorkerStateTracker()
    {
        RegisterWorker(OrderWorkerName);
        RegisterWorker(ReportWorkerName);
    }

    public void RegisterWorker(string workerName)
    {
        _states.GetOrAdd(workerName, static name => new MutableWorkerState(name));
    }

    public void MarkStarted(string workerName)
    {
        var state = _states.GetOrAdd(workerName, static name => new MutableWorkerState(name));
        lock (state.SyncRoot)
        {
            state.IsRunning = true;
            state.StartedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void MarkStopped(string workerName)
    {
        var state = _states.GetOrAdd(workerName, static name => new MutableWorkerState(name));
        lock (state.SyncRoot)
        {
            state.IsRunning = false;
        }
    }

    public void RecordIterationSuccess(string workerName)
    {
        var state = _states.GetOrAdd(workerName, static name => new MutableWorkerState(name));
        lock (state.SyncRoot)
        {
            state.CompletedIterations++;
            state.LastIterationCompletedAtUtc = DateTimeOffset.UtcNow;
            state.LastIterationSucceeded = true;
            state.LastErrorMessage = null;
        }
    }

    public void RecordRetryAttempt(string workerName, int attemptNumber, string errorMessage)
    {
        var state = _states.GetOrAdd(workerName, static name => new MutableWorkerState(name));
        lock (state.SyncRoot)
        {
            state.TotalRetryCount++;
            state.LastErrorMessage = $"Retry #{attemptNumber}: {errorMessage}";
        }
    }

    public void RecordIterationFailure(string workerName, string errorMessage)
    {
        var state = _states.GetOrAdd(workerName, static name => new MutableWorkerState(name));
        lock (state.SyncRoot)
        {
            state.FailedIterations++;
            state.LastIterationCompletedAtUtc = DateTimeOffset.UtcNow;
            state.LastIterationSucceeded = false;
            state.LastErrorMessage = errorMessage;
        }
    }

    public bool SetHeartbeatPaused(string workerName, bool paused)
    {
        if (!_states.TryGetValue(workerName, out var state))
        {
            return false;
        }

        lock (state.SyncRoot)
        {
            state.IsHeartbeatPaused = paused;
            return true;
        }
    }

    public bool SetForcedFailure(string workerName, bool enabled)
    {
        if (!_states.TryGetValue(workerName, out var state))
        {
            return false;
        }

        lock (state.SyncRoot)
        {
            state.IsForcedFailureEnabled = enabled;
            return true;
        }
    }

    public bool RecoverWorker(string workerName)
    {
        if (!_states.TryGetValue(workerName, out var state))
        {
            return false;
        }

        lock (state.SyncRoot)
        {
            state.IsHeartbeatPaused = false;
            state.IsForcedFailureEnabled = false;
            state.LastIterationSucceeded = true;
            state.LastErrorMessage = null;
            return true;
        }
    }

    public WorkerSnapshot? GetSnapshot(string workerName)
    {
        if (!_states.TryGetValue(workerName, out var state))
        {
            return null;
        }

        return ToSnapshot(state);
    }

    public IReadOnlyList<WorkerSnapshot> GetAllSnapshots()
    {
        return _states.Values
            .OrderBy(s => s.WorkerName, StringComparer.OrdinalIgnoreCase)
            .Select(ToSnapshot)
            .ToList();
    }

    private static WorkerSnapshot ToSnapshot(MutableWorkerState state)
    {
        lock (state.SyncRoot)
        {
            return new WorkerSnapshot(
                state.WorkerName,
                state.IsRunning,
                state.StartedAtUtc,
                state.LastIterationCompletedAtUtc,
                state.CompletedIterations,
                state.TotalRetryCount,
                state.FailedIterations,
                state.LastIterationSucceeded,
                state.LastErrorMessage,
                state.IsHeartbeatPaused,
                state.IsForcedFailureEnabled);
        }
    }
}
