using PocHealthCheck.WorkerWeb.Heartbeat;
using PocHealthCheck.WorkerWeb.Models;
using PocHealthCheck.WorkerWeb.Resilience;
using Polly;
using Polly.Registry;

namespace PocHealthCheck.WorkerWeb.Workers;

public sealed class ReportGenerationWorker(
    ILogger<ReportGenerationWorker> logger,
    WorkerStateTracker stateTracker,
    HeartbeatRegistry heartbeatRegistry,
    ResiliencePipelineProvider<string> pipelineProvider) : BackgroundService
{
    private const string WorkerName = WorkerStateTracker.ReportWorkerName;
    private int _cycleNumber;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pipeline = pipelineProvider.GetPipeline(WorkerResilienceExtensions.BackgroundWorkerPipelineName);

        stateTracker.MarkStarted(WorkerName);
        heartbeatRegistry.Register(
            WorkerName,
            degradedAfter: TimeSpan.FromSeconds(14),
            unhealthyAfter: TimeSpan.FromSeconds(26),
            initialMessage: "ReportGenerationWorker uruchomiony.");

        logger.LogInformation("[{WorkerName}] Start asynchronicznego generowania raportów KPI.", WorkerName);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _cycleNumber++;
                var snapshot = stateTracker.GetSnapshot(WorkerName);

                if (snapshot is { IsHeartbeatPaused: false })
                {
                    heartbeatRegistry.Beat(WorkerName, $"Start agregacji raportu KPI #{_cycleNumber}");
                }

                var context = ResilienceContextPool.Shared.Get(stoppingToken);
                context.Properties.Set(WorkerResilienceExtensions.WorkerNameKey, WorkerName);

                try
                {
                    var attemptInCurrentCycle = 0;

                    await pipeline.ExecuteAsync(async ctx =>
                    {
                        attemptInCurrentCycle++;
                        await GenerateAnalyticalReportAsync(_cycleNumber, attemptInCurrentCycle, ctx.CancellationToken);
                    }, context);

                    stateTracker.RecordIterationSuccess(WorkerName);

                    var afterSnapshot = stateTracker.GetSnapshot(WorkerName);
                    if (afterSnapshot is { IsHeartbeatPaused: false })
                    {
                        heartbeatRegistry.Beat(WorkerName, $"Wygenerowano raport analityczny #{_cycleNumber}");
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
                        "[{WorkerName}] Cykl #{Cycle} zakończony błędem po wyczerpaniu wszystkich prób retry.",
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
        catch (OperationCanceledException)
        {
            // Zamykanie usługi systemowej (SIGTERM / Windows SCM Stop)
        }
        finally
        {
            stateTracker.MarkStopped(WorkerName);
            logger.LogInformation("[{WorkerName}] Zatrzymano serwis generowania raportów.", WorkerName);
        }
    }

    private async Task GenerateAnalyticalReportAsync(int cycleNumber, int attemptNumber, CancellationToken cancellationToken)
    {
        var snapshot = stateTracker.GetSnapshot(WorkerName);
        if (snapshot is { IsForcedFailureEnabled: true })
        {
            await Task.Delay(120, cancellationToken);
            throw new InvalidOperationException("Wymuszony błąd hurtowni danych raportowych (tryb symulacji).");
        }

        // Etap 1: Asynchroniczna agregacja metryk sprzedażowych
        await Task.Delay(Random.Shared.Next(200, 450), cancellationToken);

        // Co 5. cykl symulujemy chwilową niedostępność zasobu podczas 1. próby (naprawianą przez Retry)
        if (cycleNumber % 5 == 0 && attemptNumber == 1)
        {
            throw new TransientWorkerException(
                $"Chwilowa blokada odczytu widoku analitycznego podczas generowania raportu #{cycleNumber}.");
        }

        // Etap 2: Renderowanie podsumowania raportu
        var aggregatedRows = Random.Shared.Next(120, 950);
        await Task.Delay(Random.Shared.Next(120, 250), cancellationToken);

        logger.LogInformation(
            "[{WorkerName}] Cykl #{Cycle} (próba #{Attempt}): wygenerowano raport KPI z {Rows} wierszy.",
            WorkerName,
            cycleNumber,
            attemptNumber,
            aggregatedRows);
    }
}
