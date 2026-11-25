using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PocHealthCheck.WorkerWeb.HealthChecks;
using PocHealthCheck.WorkerWeb.Heartbeat;
using PocHealthCheck.WorkerWeb.Models;
using PocHealthCheck.WorkerWeb.Resilience;
using PocHealthCheck.WorkerWeb.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<WorkerStateTracker>();
builder.Services.AddSingleton<HeartbeatRegistry>();

builder.Services.AddWorkerResiliencePipelines();

builder.Services.AddHttpClient("HeartbeatClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
});

// Rejestracja 2 głównych serwisów BackgroundService + serwisu publikującego Heartbeat
builder.Services.AddHostedService<OrderProcessingWorker>();
builder.Services.AddHostedService<ReportGenerationWorker>();
builder.Services.AddHostedService<HeartbeatSenderService>();

// Mechanizm HealthCheck dla całego serwisu, poszczególnych BackgroundService oraz Watchdoga Heartbeat
builder.Services.AddHealthChecks()
    .AddCheck<ServiceOverallHealthCheck>(
        name: "service-overall",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["service", "overall"])
    .Add(new HealthCheckRegistration(
        name: "worker-order-processing",
        factory: sp => new BackgroundWorkerHealthCheck(
            WorkerStateTracker.OrderWorkerName,
            sp.GetRequiredService<WorkerStateTracker>(),
            sp.GetRequiredService<HeartbeatRegistry>()),
        failureStatus: HealthStatus.Unhealthy,
        tags: ["worker", "orders"]))
    .Add(new HealthCheckRegistration(
        name: "worker-report-generation",
        factory: sp => new BackgroundWorkerHealthCheck(
            WorkerStateTracker.ReportWorkerName,
            sp.GetRequiredService<WorkerStateTracker>(),
            sp.GetRequiredService<HeartbeatRegistry>()),
        failureStatus: HealthStatus.Unhealthy,
        tags: ["worker", "reports"]))
    .AddCheck<HeartbeatWatchdogHealthCheck>(
        name: "heartbeat-watchdog",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["heartbeat", "watchdog"]);

var app = builder.Build();

// Główny endpoint HealthCheck (kompatybilny z HealthChecks UI Dashboard w Web API)
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Dedykowane pod-endpointy HealthCheck dla całego serwisu oraz wyłącznie dla BackgroundServices
app.MapHealthChecks("/health/service", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("service"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/workers", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("worker"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapGet("/", (WorkerStateTracker stateTracker, HeartbeatRegistry heartbeatRegistry) =>
{
    var now = DateTimeOffset.UtcNow;
    return Results.Ok(new
    {
        service = "PocHealthCheck.WorkerWeb (.NET 8)",
        startedAtUtc = heartbeatRegistry.ServiceStartedAtUtc,
        uptimeSeconds = Math.Round((now - heartbeatRegistry.ServiceStartedAtUtc).TotalSeconds, 1),
        endpoints = new
        {
            allHealthChecks = "/health",
            serviceOnlyHealth = "/health/service",
            workersOnlyHealth = "/health/workers",
            heartbeats = "/heartbeats",
            simulatePauseHeartbeat = "GET/POST /simulation/{workerName}/pause-heartbeat",
            simulateForceFailure = "GET/POST /simulation/{workerName}/force-failure",
            simulateRecover = "GET/POST /simulation/{workerName}/recover"
        },
        workers = stateTracker.GetAllSnapshots(),
        heartbeats = heartbeatRegistry.GetAll().Select(h => new
        {
            h.ComponentName,
            h.LastBeatUtc,
            ageSeconds = Math.Round(h.ElapsedSinceLastBeat(now).TotalSeconds, 2),
            h.SequenceNumber,
            h.StatusMessage
        })
    });
});

app.MapGet("/heartbeats", (HeartbeatRegistry heartbeatRegistry) =>
{
    var now = DateTimeOffset.UtcNow;
    return Results.Ok(heartbeatRegistry.GetAll().Select(h => new
    {
        h.ComponentName,
        h.LastBeatUtc,
        ageSeconds = Math.Round(h.ElapsedSinceLastBeat(now).TotalSeconds, 2),
        degradedThresholdSeconds = h.DegradedAfter.TotalSeconds,
        unhealthyThresholdSeconds = h.UnhealthyAfter.TotalSeconds,
        h.SequenceNumber,
        h.StatusMessage
    }));
});

app.MapMethods("/simulation/{workerName}/pause-heartbeat", ["GET", "POST"], (string workerName, WorkerStateTracker tracker) =>
    tracker.SetHeartbeatPaused(workerName, paused: true)
        ? Results.Ok(new
        {
            worker = workerName,
            simulation = "pause-heartbeat",
            message = $"Wstrzymano wysyłanie heartbeat dla '{workerName}'. Za kilkanaście sekund status zmieni się na Degraded/Unhealthy."
        })
        : Results.NotFound(new { error = $"Nieznany worker '{workerName}'. Dostępne: OrderProcessingWorker, ReportGenerationWorker." }));

app.MapMethods("/simulation/{workerName}/force-failure", ["GET", "POST"], (string workerName, WorkerStateTracker tracker) =>
    tracker.SetForcedFailure(workerName, enabled: true)
        ? Results.Ok(new
        {
            worker = workerName,
            simulation = "force-failure",
            message = $"Włączono symulację ciągłego błędu dla '{workerName}'. W kolejnym cyklu (do 6s) worker wykona 3 próby retry (Polly), a po ich wyczerpaniu zgłosi Unhealthy."
        })
        : Results.NotFound(new { error = $"Nieznany worker '{workerName}'. Dostępne: OrderProcessingWorker, ReportGenerationWorker." }));

app.MapMethods("/simulation/{workerName}/recover", ["GET", "POST"], (string workerName, WorkerStateTracker tracker, HeartbeatRegistry heartbeats) =>
{
    if (!tracker.RecoverWorker(workerName))
    {
        return Results.NotFound(new { error = $"Nieznany worker '{workerName}'. Dostępne: OrderProcessingWorker, ReportGenerationWorker." });
    }

    heartbeats.Beat(workerName, "Przywrócono normalną pracę z trybu symulacji.");
    return Results.Ok(new
    {
        worker = workerName,
        simulation = "recover",
        message = $"Przywrócono prawidłowe działanie i heartbeat dla '{workerName}'."
    });
});

app.Run();
