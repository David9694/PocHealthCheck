# PoC Health Checks, BackgroundServices, Retry & Heartbeat (.NET 10)

Rozwiązanie składa się z dwóch projektów webowych w **.NET 10**:

1. **`PocHealthCheck.WorkerWeb`** (`http://localhost:5200`) – serwis typu Web zawierający:
   - **2 serwisy `BackgroundService`** wykonujące asynchroniczne zadania cykliczne:
     - `OrderProcessingWorker` – asynchroniczne przetwarzanie paczek zamówień (co 4 s).
     - `ReportGenerationWorker` – asynchroniczna agregacja i generowanie raportów KPI (co 6 s).
   - **Mechanizm Retry (`Polly` / `Microsoft.Extensions.Resilience`)**:
     - `ResiliencePipeline` z wykładniczym backoffem (`Exponential`), jitterem, maks. 3 ponowieniami oraz rejestrowaniem prób retry w metrykach health checka.
     - Co kilka cykli workery symulują pojedynczy błąd przejściowy (`TransientWorkerException`), który jest automatycznie naprawiany przez retry.
   - **Mechanizm Heartbeat**:
     - Wewnętrzny `HeartbeatRegistry` monitorowany przez `HeartbeatWatchdogHealthCheck` oraz indywidualne health checki workerów.
     - Zewnętrzny `HeartbeatSenderService` wysyłający co 4 s aktywny puls (`POST /api/heartbeats`) do `PocHealthCheck.Api`.
   - **Health Checks**:
     - `service-overall` – kondycja całego serwisu `WorkerWeb` (RAM, uptime, zagregowany stan wszystkich workerów).
     - `worker-order-processing` – kondycja `OrderProcessingWorker`.
     - `worker-report-generation` – kondycja `ReportGenerationWorker`.
     - `heartbeat-watchdog` – kontrola świeżości sygnałów heartbeat wszystkich komponentów.

2. **`PocHealthCheck.Api`** (`http://localhost:5100`) – Web API zawierające:
   - Testowe endpointy API: `GET /api/test` oraz `GET /api/orders/preview`.
   - Odbiornik i podgląd sygnałów Heartbeat z serwisu workerowego: `POST /api/heartbeats` oraz `GET /api/heartbeats`.
   - **Health Checks UI Dashboard** pod adresem `http://localhost:5100/healthchecks-ui` odpytujący co 5 s zarówno `PocHealthCheck.WorkerWeb` (`http://localhost:5200/health`), jak i własny endpoint `/health`.

## Uruchomienie

Uruchom oba projekty w dwóch osobnych terminalach z katalogu głównego:

```bash
# Terminal 1 - Serwis WorkerWeb (port 5200)
dotnet run --project PocHealthCheck.WorkerWeb

# Terminal 2 - Web API + HealthChecks UI Dashboard (port 5100)
dotnet run --project PocHealthCheck.Api
```

## Najważniejsze adresy URL

- **Health Checks UI Dashboard**: `http://localhost:5100/healthchecks-ui`
- **Testowy endpoint Web API**: `http://localhost:5100/api/test`
- **Podgląd odebranych Heartbeatów w API**: `http://localhost:5100/api/heartbeats`
- **HealthCheck całego serwisu WorkerWeb i workerów (JSON)**: `http://localhost:5200/health`
- **HealthCheck wyłącznie BackgroundServices**: `http://localhost:5200/health/workers`
- **Podsumowanie stanu WorkerWeb**: `http://localhost:5200/`

## Testowanie awarii (Retry & Heartbeat) na żywo

Endpointy symulacyjne obsługują zarówno `GET` (można wkleić bezpośrednio w przeglądarkę), jak i `POST` (`curl`):

```bash
# 1. Wstrzymanie heartbeatu wybranego BackgroundService (po 10s Degraded, po 20s Unhealthy w UI):
curl http://localhost:5200/simulation/OrderProcessingWorker/pause-heartbeat

# 2. Wymuszenie ciągłego błędu (Polly wykona 3 próby Retry i oznaczy worker jako Unhealthy):
curl http://localhost:5200/simulation/ReportGenerationWorker/force-failure

# 3. Przywrócenie normalnej pracy workerów:
curl http://localhost:5200/simulation/OrderProcessingWorker/recover
curl http://localhost:5200/simulation/ReportGenerationWorker/recover
```
