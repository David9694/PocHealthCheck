using Microsoft.Extensions.Diagnostics.HealthChecks;
using PocHealthCheck.WorkerWeb.Heartbeat;
using PocHealthCheck.WorkerWeb.Models;

namespace PocHealthCheck.WorkerWeb.HealthChecks;

public sealed class BackgroundWorkerHealthCheck(
    string workerName,
    WorkerStateTracker stateTracker,
    HeartbeatRegistry heartbeatRegistry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = stateTracker.GetSnapshot(workerName);

        if (snapshot is null)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Nie znaleziono zarejestrowanego serwisu BackgroundService o nazwie '{workerName}'."));
        }

        var heartbeat = heartbeatRegistry.Get(workerName);
        var heartbeatAgeSeconds = heartbeat is not null
            ? Math.Round(heartbeat.ElapsedSinceLastBeat(now).TotalSeconds, 2)
            : -1d;

        var data = new Dictionary<string, object>
        {
            ["workerName"] = snapshot.WorkerName,
            ["isRunning"] = snapshot.IsRunning,
            ["completedIterations"] = snapshot.CompletedIterations,
            ["totalRetryCount"] = snapshot.TotalRetryCount,
            ["failedIterations"] = snapshot.FailedIterations,
            ["lastIterationSucceeded"] = snapshot.LastIterationSucceeded,
            ["lastHeartbeatAgeSeconds"] = heartbeatAgeSeconds,
            ["heartbeatSequence"] = heartbeat?.SequenceNumber ?? 0,
            ["lastErrorMessage"] = snapshot.LastErrorMessage ?? "brak"
        };

        if (!snapshot.IsRunning)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"BackgroundService '{workerName}' nie jest uruchomiony.",
                data: data));
        }

        if (!snapshot.LastIterationSucceeded)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"BackgroundService '{workerName}' zakończył ostatni cykl błędem po wyczerpaniu prób retry: {snapshot.LastErrorMessage}",
                data: data));
        }

        if (heartbeat is not null)
        {
            var elapsed = heartbeat.ElapsedSinceLastBeat(now);
            if (elapsed >= heartbeat.UnhealthyAfter)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Brak sygnału heartbeat z '{workerName}' od {elapsed.TotalSeconds:F1}s (próg krytyczny: {heartbeat.UnhealthyAfter.TotalSeconds:F0}s).",
                    data: data));
            }

            if (elapsed >= heartbeat.DegradedAfter)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Opóźniony sygnał heartbeat z '{workerName}': {elapsed.TotalSeconds:F1}s (próg ostrzegawczy: {heartbeat.DegradedAfter.TotalSeconds:F0}s).",
                    data: data));
            }
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"'{workerName}' pracuje stabilnie (cykle: {snapshot.CompletedIterations}, retry: {snapshot.TotalRetryCount}, puls: {heartbeatAgeSeconds:F1}s temu).",
            data: data));
    }
}
