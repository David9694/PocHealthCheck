using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PocHealthCheck.Api.HealthChecks;
using PocHealthCheck.Api.Heartbeat;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<RemoteHeartbeatStore>();

var startedAtUtc = DateTimeOffset.UtcNow;

// Health checks samego Web API oraz odbiornika zdalnych sygnałów Heartbeat
builder.Services.AddHealthChecks()
    .AddCheck(
        name: "api-self",
        check: () => HealthCheckResult.Healthy(
            $"PocHealthCheck.Api działa poprawnie (uptime: {(DateTimeOffset.UtcNow - startedAtUtc).TotalSeconds:F0}s)."),
        tags: ["api", "self"])
    .AddCheck<RemoteHeartbeatHealthCheck>(
        name: "remote-worker-heartbeat-push",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["heartbeat", "remote"]);

// Konfiguracja Health Checks UI Dashboard pobierającego health z serwisu WorkerWeb oraz z Web API
var workerHealthUrl = builder.Configuration["Monitoring:WorkerHealthUrl"] ?? "http://localhost:5200/health";
var apiHealthUrl = builder.Configuration["Monitoring:ApiHealthUrl"] ?? "http://localhost:5100/health";

builder.Services
    .AddHealthChecksUI(setup =>
    {
        setup.SetHeaderText("POC .NET 8 - Health Checks & Background Services Monitor");
        setup.SetEvaluationTimeInSeconds(5);
        setup.SetMinimumSecondsBetweenFailureNotifications(30);
        setup.MaximumHistoryEntriesPerEndpoint(50);

        setup.AddHealthCheckEndpoint("WorkerWeb Service & BackgroundWorkers", workerHealthUrl);
        setup.AddHealthCheckEndpoint("Web API & Heartbeat Receiver", apiHealthUrl);
    })
    .AddInMemoryStorage();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new
{
    application = "PocHealthCheck.Api (.NET 8)",
    dashboardUrl = "/healthchecks-ui",
    endpoints = new
    {
        testEndpoint = "/api/test",
        sampleOrders = "/api/orders/preview",
        receivedHeartbeats = "/api/heartbeats",
        apiHealth = "/health",
        healthChecksUiDashboard = "/healthchecks-ui",
        swaggerUi = "/swagger"
    }
}));

// 1) Testowe endpointy Web API
app.MapGet("/api/test", (RemoteHeartbeatStore heartbeatStore) =>
{
    var workerHeartbeats = heartbeatStore.GetAll();
    return Results.Ok(new
    {
        message = "Testowy endpoint Web API (.NET 8) działa prawidłowo!",
        framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        serverTimeUtc = DateTimeOffset.UtcNow,
        connectedWorkerServices = workerHeartbeats.Count,
        latestWorkerPulse = workerHeartbeats.Select(h => new
        {
            h.ServiceName,
            h.InstanceId,
            h.ReceivedAtUtc,
            h.ReceivedCount
        })
    });
})
.WithName("GetTestStatus");

app.MapGet("/api/orders/preview", () =>
{
    var items = Enumerable.Range(1, 5).Select(index => new
    {
        OrderId = $"ORD-{1000 + index}",
        Customer = $"Klient #{index}",
        AmountPln = Math.Round(120m * index + 49.99m, 2),
        Status = index % 2 == 0 ? "ProcessingInBackground" : "Queued",
        CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-index * 3)
    });

    return Results.Ok(items);
})
.WithName("GetOrdersPreview");

// 2) Endpointy mechanizmu Heartbeat (Push z WorkerWeb + podgląd)
app.MapPost("/api/heartbeats", (IncomingHeartbeatDto payload, RemoteHeartbeatStore store) =>
{
    var stored = store.Record(payload);
    return Results.Accepted($"/api/heartbeats", new
    {
        status = "Heartbeat zarejestrowany",
        stored.ServiceName,
        stored.ReceivedCount,
        stored.ReceivedAtUtc
    });
})
.WithName("ReceiveHeartbeat");

app.MapGet("/api/heartbeats", (RemoteHeartbeatStore store) =>
{
    var now = DateTimeOffset.UtcNow;
    var result = store.GetAll().Select(h => new
    {
        h.ServiceName,
        h.InstanceId,
        h.ReceivedAtUtc,
        secondsSinceLastBeat = Math.Round(h.ElapsedSinceReceived(now).TotalSeconds, 2),
        h.ReceivedCount,
        h.LastPayload.UptimeSeconds,
        h.LastPayload.Components,
        h.LastPayload.Workers
    });

    return Results.Ok(result);
})
.WithName("GetReceivedHeartbeats");

// 3) Endpoint HealthCheck Web API + Health Checks UI Dashboard
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecksUI(options =>
{
    options.UIPath = "/healthchecks-ui";
    options.ApiPath = "/healthchecks-api";
});

app.Run();
