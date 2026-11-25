using Microsoft.Extensions.Diagnostics.HealthChecks;
using PocHealthCheck.WorkerWeb.Heartbeat;
using PocHealthCheck.WorkerWeb.Models;

namespace PocHealthCheck.WorkerWeb.HealthChecks;

public sealed class ServiceOverallHealthCheck(
    WorkerStateTracker stateTracker,
    HeartbeatRegistry heartbeatRegistry) : IHealthCheck
{
    private const long MemoryDegradedThresholdBytes = 512L * 1024 * 1024; // 512 MB

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var uptime = now - heartbeatRegistry.ServiceStartedAtUtc;
        var allocatedBytes = GC.GetTotalMemory(forceFullCollection: false);
        var allocatedMb = Math.Round(allocatedBytes / (1024.0 * 1024.0), 2);

        var snapshots = stateTracker.GetAllSnapshots();
        var runningCount = snapshots.Count(w => w.IsRunning);
        var failingWorkers = snapshots.Where(w => !w.IsRunning || !w.LastIterationSucceeded).ToList();

        var data = new Dictionary<string, object>
        {
            ["serviceName"] = "PocHealthCheck.WorkerWeb",
            ["uptimeSeconds"] = Math.Round(uptime.TotalSeconds, 1),
            ["allocatedMemoryMb"] = allocatedMb,
            ["registeredWorkers"] = snapshots.Count,
            ["runningWorkers"] = runningCount,
            ["totalIterationsAcrossWorkers"] = snapshots.Sum(w => w.CompletedIterations),
            ["totalRetriesAcrossWorkers"] = snapshots.Sum(w => w.TotalRetryCount)
        };

        if (runningCount == 0 && snapshots.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Żaden z serwisów BackgroundService nie jest obecnie uruchomiony.",
                data: data));
        }

        if (failingWorkers.Count == snapshots.Count && snapshots.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Wszystkie serwisy BackgroundService ({failingWorkers.Count}) zgłaszają błędy wykonania.",
                data: data));
        }

        if (failingWorkers.Count > 0)
        {
            var names = string.Join(", ", failingWorkers.Select(w => w.WorkerName));
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Serwis działa w trybie zdegradowanym — problemy w workerach: {names}.",
                data: data));
        }

        if (allocatedBytes > MemoryDegradedThresholdBytes)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Podwyższone zużycie pamięci procesu: {allocatedMb} MB.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Serwis WorkerWeb działa prawidłowo ({runningCount}/{snapshots.Count} aktywnych workerów, RAM: {allocatedMb} MB).",
            data: data));
    }
}
