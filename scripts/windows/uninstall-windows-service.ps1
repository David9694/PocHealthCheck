param(
    [string]$ServiceName = "PocHealthCheck.WorkerWeb"
)

$ErrorActionPreference = "Stop"

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "==> Zatrzymywanie usługi '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Write-Host "==> Usuwanie usługi '$ServiceName'..."
    sc.exe delete $ServiceName
    Write-Host "==> Usługa Windows '$ServiceName' została usunięta."
} else {
    Write-Host "Usługa '$ServiceName' nie jest zainstalowana."
}
