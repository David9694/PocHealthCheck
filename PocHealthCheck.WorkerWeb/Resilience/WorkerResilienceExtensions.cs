using PocHealthCheck.WorkerWeb.Heartbeat;
using PocHealthCheck.WorkerWeb.Models;
using Polly;
using Polly.Retry;

namespace PocHealthCheck.WorkerWeb.Resilience;

public static class WorkerResilienceExtensions
{
    public static readonly ResiliencePropertyKey<string> WorkerNameKey = new("WorkerName");

    public const string BackgroundWorkerPipelineName = "background-worker-retry";

    public static IServiceCollection AddWorkerResiliencePipelines(this IServiceCollection services)
    {
        services.AddResiliencePipeline(BackgroundWorkerPipelineName, (builder, context) =>
        {
            var logger = context.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("WorkerRetryPipeline");
            var stateTracker = context.ServiceProvider.GetRequiredService<WorkerStateTracker>();
            var heartbeatRegistry = context.ServiceProvider.GetRequiredService<HeartbeatRegistry>();

            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(400),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not OperationCanceledException),
                OnRetry = args =>
                {
                    var workerName = args.Context.Properties.TryGetValue(WorkerNameKey, out var name)
                        ? name
                        : "UnknownWorker";
                    var attempt = args.AttemptNumber + 1;
                    var errorMessage = args.Outcome.Exception?.Message ?? "Nieznany błąd podczas wykonywania zadania";

                    stateTracker.RecordRetryAttempt(workerName, attempt, errorMessage);

                    var snapshot = stateTracker.GetSnapshot(workerName);
                    if (snapshot is { IsHeartbeatPaused: false })
                    {
                        heartbeatRegistry.Beat(workerName, $"Ponawianie próby #{attempt} po błędzie przejściowym");
                    }

                    logger.LogWarning(
                        args.Outcome.Exception,
                        "[{WorkerName}] Próba ponowienia #{Attempt} za {DelayMs:F0} ms z powodu błędu: {Error}",
                        workerName,
                        attempt,
                        args.RetryDelay.TotalMilliseconds,
                        errorMessage);

                    return ValueTask.CompletedTask;
                }
            });
        });

        return services;
    }
}
