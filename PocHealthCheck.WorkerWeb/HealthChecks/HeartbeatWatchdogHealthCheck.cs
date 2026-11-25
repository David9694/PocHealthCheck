using Microsoft.Extensions.Diagnostics.HealthChecks;
using PocHealthCheck.WorkerWeb.Heartbeat;

namespace PocHealthCheck.WorkerWeb.HealthChecks;

public sealed class HeartbeatWatchdogHealthCheck(HeartbeatRegistry heartbeatRegistry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = heartbeatRegistry.GetAll();

        if (entries.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Rejestr heartbeat jest pusty — trwa inicjalizacja komponentów."));
        }

        var data = new Dictionary<string, object>();
        var unhealthyComponents = new List<string>();
        var degradedComponents = new List<string>();

        foreach (var entry in entries)
        {
            var age = entry.ElapsedSinceLastBeat(now);
            data[$"{entry.ComponentName}.ageSeconds"] = Math.Round(age.TotalSeconds, 2);
            data[$"{entry.ComponentName}.sequence"] = entry.SequenceNumber;
            data[$"{entry.ComponentName}.status"] = entry.StatusMessage;

            if (age >= entry.UnhealthyAfter)
            {
                unhealthyComponents.Add($"{entry.ComponentName} ({age.TotalSeconds:F1}s)");
            }
            else if (age >= entry.DegradedAfter)
            {
                degradedComponents.Add($"{entry.ComponentName} ({age.TotalSeconds:F1}s)");
            }
        }

        if (unhealthyComponents.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Utracono sygnał heartbeat dla komponentów: {string.Join(", ", unhealthyComponents)}.",
                data: data));
        }

        if (degradedComponents.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Opóźniony sygnał heartbeat dla komponentów: {string.Join(", ", degradedComponents)}.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Wszystkie sygnały heartbeat ({entries.Count}) są aktualne.",
            data: data));
    }
}
