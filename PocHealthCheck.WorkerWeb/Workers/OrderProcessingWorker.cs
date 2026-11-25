using PocHealthCheck.WorkerWeb.Heartbeat;
using PocHealthCheck.WorkerWeb.Models;
using PocHealthCheck.WorkerWeb.Resilience;
using Polly;
using Polly.Registry;

namespace PocHealthCheck.WorkerWeb.Workers;

public sealed class OrderProcessingWorker(
    ILogger<OrderProcessingWorker> logger,
    WorkerStateTracker stateTracker,
    HeartbeatRegistry heartbeatRegistry,
    ResiliencePipelineProvider<string> pipelineProvider) : BackgroundService
{
    private const string WorkerName = WorkerStateTracker.OrderWorkerName;
    private int _cycleNumber;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pipeline = pipelineProvider.GetPipeline(WorkerResilienceExtensions.BackgroundWorkerPipelineName);

        stateTracker.MarkStarted(WorkerName);
        heartbeatRegistry.Register(
            WorkerName,
            degradedAfter: TimeSpan.FromSeconds(10),
            unhealthyAfter: TimeSpan.FromSeconds(20),
            initialMessage: "OrderProcessingWorker uruchomiony.");

        logger.LogInformation("[{WorkerName}] Start asynchronicznego przetwarzania zamówień.", WorkerName);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _cycleNumber++;
                var snapshot = stateTracker.GetSnapshot(WorkerName);

                if (snapshot is { IsHeartbeatPaused: false })
                {
                    heartbeatRegistry.Beat(WorkerName, $"Start cyklu #{_cycleNumber} przetwarzania zamówień");
                }

                var context = ResilienceContextPool.Shared.Get(stoppingToken);
                context.Properties.Set(WorkerResilienceExtensions.WorkerNameKey, WorkerName);

                try
                {
                    var attemptInCurrentCycle = 0;

                    await pipeline.ExecuteAsync(async ctx =>
                    {
                        attemptInCurrentCycle++;
                        await ProcessOrdersBatchAsync(_cycleNumber, attemptInCurrentCycle, ctx.CancellationToken);
                    }, context);

                    stateTracker.RecordIterationSuccess(WorkerName);

                    var afterSnapshot = stateTracker.GetSnapshot(WorkerName);
                    if (afterSnapshot is { IsHeartbeatPaused: false })
                    {
                        heartbeatRegistry.Beat(WorkerName, $"Pomyślnie przetworzono paczkę zamówień #{_cycleNumber}");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    stateTracker.RecordIterationFailure(WorkerName, ex.Message);
                    logger.LogError(
                        ex,
                        "[{WorkerName}] Cykl #{Cycle} zakończony niepowodzeniem po wyczerpaniu prób retry.",
                        WorkerName,
                        _cycleNumber);
                }
                finally
                {
                    ResilienceContextPool.Shared.Return(context);
                }

                await timer.WaitForNextTickAsync(stoppingToken);
            }
        }
        finally
        {
            stateTracker.MarkStopped(WorkerName);
            logger.LogInformation("[{WorkerName}] Zatrzymano serwis przetwarzania zamówień.", WorkerName);
        }
    }

    private async Task ProcessOrdersBatchAsync(int cycleNumber, int attemptNumber, CancellationToken cancellationToken)
    {
        var snapshot = stateTracker.GetSnapshot(WorkerName);
        if (snapshot is { IsForcedFailureEnabled: true })
        {
            await Task.Delay(120, cancellationToken);
            throw new InvalidOperationException("Wymuszony błąd krytyczny bramki płatności (tryb symulacji).");
        }

        // Asynchroniczne pobieranie i walidacja przykładowych zamówień
        var ordersCount = Random.Shared.Next(3, 12);
        await Task.Delay(Random.Shared.Next(150, 350), cancellationToken);

        // Co 4. cykl symulujemy przejściowy błąd połączenia w 1. próbie, aby pokazać działanie mechanizmu Retry
        if (cycleNumber % 4 == 0 && attemptNumber == 1)
        {
            throw new TransientWorkerException(
                $"Przejściowy timeout podczas rezerwacji stanów magazynowych dla {ordersCount} zamówień (cykl #{cycleNumber}).");
        }

        // Asynchroniczny zapis potwierdzeń zamówień
        await Task.Delay(Random.Shared.Next(100, 220), cancellationToken);

        logger.LogInformation(
            "[{WorkerName}] Cykl #{Cycle} (próba #{Attempt}): przetworzono {OrdersCount} zamówień asynchronicznie.",
            WorkerName,
            cycleNumber,
            attemptNumber,
            ordersCount);
    }
}
