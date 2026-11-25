using Microsoft.Extensions.Diagnostics.HealthChecks;
using PocHealthCheck.Api.Heartbeat;

namespace PocHealthCheck.Api.HealthChecks;

public sealed class RemoteHeartbeatHealthCheck(RemoteHeartbeatStore store) : IHealthCheck
{
    private static readonly TimeSpan DegradedThreshold = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan UnhealthyThreshold = TimeSpan.FromSeconds(24);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var records = store.GetAll();

        if (records.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Oczekiwanie na pierwszy sygnał heartbeat z serwisu PocHealthCheck.WorkerWeb."));
        }

        var data = new Dictionary<string, object>();
        var unhealthy = new List<string>();
        var degraded = new List<string>();

        foreach (var item in records)
        {
            var elapsed = item.ElapsedSinceReceived(now);
            data[$"{item.ServiceName}.instanceId"] = item.InstanceId;
            data[$"{item.ServiceName}.receivedCount"] = item.ReceivedCount;
            data[$"{item.ServiceName}.lastReceivedSecondsAgo"] = Math.Round(elapsed.TotalSeconds, 2);
            data[$"{item.ServiceName}.workerUptimeSeconds"] = item.LastPayload.UptimeSeconds;

            if (elapsed >= UnhealthyThreshold)
            {
                unhealthy.Add($"{item.ServiceName} ({elapsed.TotalSeconds:F1}s bez pulsu)");
            }
            else if (elapsed >= DegradedThreshold)
            {
                degraded.Add($"{item.ServiceName} ({elapsed.TotalSeconds:F1}s bez pulsu)");
            }
        }

        if (unhealthy.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Przekroczono czas oczekiwania na heartbeat ze zdalnych serwisów: {string.Join(", ", unhealthy)}.",
                data: data));
        }

        if (degraded.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Opóźniony heartbeat ze zdalnych serwisów: {string.Join(", ", degraded)}.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Odbiór heartbeat aktywny z {records.Count} serwisu(ów).",
            data: data));
    }
}
