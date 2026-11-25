using System.Net.Http.Json;
using PocHealthCheck.WorkerWeb.Models;

namespace PocHealthCheck.WorkerWeb.Heartbeat;

public sealed record OutgoingComponentPulse(
    string ComponentName,
    DateTimeOffset LastBeatUtc,
    double AgeSeconds,
    long SequenceNumber,
    string StatusMessage);

public sealed record OutgoingHeartbeatPayload(
    string ServiceName,
    string InstanceId,
    DateTimeOffset SentAtUtc,
    double UptimeSeconds,
    IReadOnlyList<OutgoingComponentPulse> Components,
    IReadOnlyList<WorkerSnapshot> Workers);

public sealed class HeartbeatSenderService(
    ILogger<HeartbeatSenderService> logger,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    HeartbeatRegistry heartbeatRegistry,
    WorkerStateTracker stateTracker) : BackgroundService
{
    private readonly string _instanceId = $"{Environment.MachineName}-{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        heartbeatRegistry.Register(
            HeartbeatRegistry.ServiceComponentName,
            degradedAfter: TimeSpan.FromSeconds(10),
            unhealthyAfter: TimeSpan.FromSeconds(20),
            initialMessage: "Host serwisu WorkerWeb aktywny.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                var uptime = now - heartbeatRegistry.ServiceStartedAtUtc;

                heartbeatRegistry.Beat(
                    HeartbeatRegistry.ServiceComponentName,
                    $"Host aktywny (uptime: {uptime.TotalSeconds:F0}s)");

                await PushHeartbeatToApiAsync(now, uptime, stoppingToken);

                await timer.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Zamykanie usługi systemowej (SIGTERM / Windows SCM Stop)
        }
    }

    private async Task PushHeartbeatToApiAsync(DateTimeOffset now, TimeSpan uptime, CancellationToken cancellationToken)
    {
        var targetEndpoint = configuration["Heartbeat:ApiEndpoint"];
        if (string.IsNullOrWhiteSpace(targetEndpoint))
        {
            return;
        }

        var payload = new OutgoingHeartbeatPayload(
            ServiceName: "PocHealthCheck.WorkerWeb",
            InstanceId: _instanceId,
            SentAtUtc: now,
            UptimeSeconds: Math.Round(uptime.TotalSeconds, 1),
            Components: heartbeatRegistry.GetAll()
                .Select(e => new OutgoingComponentPulse(
                    e.ComponentName,
                    e.LastBeatUtc,
                    Math.Round(e.ElapsedSinceLastBeat(now).TotalSeconds, 2),
                    e.SequenceNumber,
                    e.StatusMessage))
                .ToList(),
            Workers: stateTracker.GetAllSnapshots());

        try
        {
            var client = httpClientFactory.CreateClient("HeartbeatClient");
            using var response = await client.PostAsJsonAsync(targetEndpoint, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                "Nie udało się wysłać sygnału heartbeat push do {Endpoint}: {Message}",
                targetEndpoint,
                ex.Message);
        }
    }
}
