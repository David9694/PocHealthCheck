param(
    [string]$ServiceName = "PocHealthCheck.WorkerWeb",
    [string]$Urls = "http://localhost:5200"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$ProjectPath = Join-Path $RepoRoot "PocHealthCheck.WorkerWeb\PocHealthCheck.WorkerWeb.csproj"
$PublishDir = Join-Path $RepoRoot "publish\workerweb"
$ExePath = Join-Path $PublishDir "PocHealthCheck.WorkerWeb.exe"

Write-Host "==> Publikowanie projektu WorkerWeb (Release) do: $PublishDir"
dotnet publish $ProjectPath -c Release -o $PublishDir

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "==> Zatrzymywanie i usuwanie istniejącej usługi '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

$BinPath = "`"$ExePath`" --urls $Urls"

Write-Host "==> Tworzenie usługi Windows Service '$ServiceName'..."
New-Service `
    -Name $ServiceName `
    -BinaryPathName $BinPath `
    -DisplayName "PoC HealthCheck WorkerWeb (.NET 8)" `
    -Description "Serwis workerowy .NET 8 z dwoma BackgroundServices, mechanizmem Retry, Heartbeat oraz HealthChecks." `
    -StartupType Automatic

# Automatyczny restart usługi przez Windows SCM w razie awarii procesu
sc.exe failure $ServiceName reset= 60 actions= restart/5000/restart/5000/restart/10000 | Out-Null

Write-Host "==> Uruchamianie usługi '$ServiceName'..."
Start-Service -Name $ServiceName
Get-Service -Name $ServiceName
