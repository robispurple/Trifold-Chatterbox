param (
    [switch]$Docker
)

# 1. Refresh PATH in this process before spawning Windows Terminal
$machinePath = [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::Machine)
$userPath = [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::User)
$env:Path = "$userPath;$machinePath"

$rootDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$names = @("Alice", "Bob", "Charlie")

if ($Docker) {
    Write-Host "Cleaning up previous client containers..." -ForegroundColor Yellow
    docker ps -a -q --filter "name=chatterbox-client-" | ForEach-Object { docker rm -f $_ } 2>$null
    docker ps -a -q --filter "ancestor=trifold/client-spectre:latest" | ForEach-Object { docker rm -f $_ } 2>$null

    # Check if a local HubServer / AspireHost is running on host port 5000
    $hubUrl = "http://host.docker.internal:5000"
    $otlpUrl = "http://host.docker.internal:19071"
    
    # Check if container chatterbox-hubserver is running or start it if no local HubServer
    $runningHubContainer = docker ps -q --filter "name=chatterbox-hubserver"
    if (-not $runningHubContainer) {
        # Check if local host has a listener on 5000
        $portOpen = (Test-NetConnection -ComputerName 127.0.0.1 -Port 5000 -InformationLevel Quiet -WarningAction SilentlyContinue)
        if (-not $portOpen) {
            Write-Host "Starting Docker HubServer backend & Aspire Dashboard..." -ForegroundColor Cyan
            docker compose -f "$rootDir\docker-compose.yml" up -d hubserver aspire-dashboard
            $hubUrl = "http://hubserver:8080"
            $otlpUrl = "http://aspire-dashboard:18889"
        } else {
            Write-Host "Detected local AspireHost / HubServer on host port 5000. Connecting containers via host.docker.internal..." -ForegroundColor Green
        }
    } else {
        $hubUrl = "http://hubserver:8080"
        $otlpUrl = "http://aspire-dashboard:18889"
    }

    if ($hubUrl -like "*hubserver:8080*") {
        $cmd1 = "docker compose run --rm --name chatterbox-client-alice -e CHAT_USERNAME=$($names[0]) client-spectre"
        $cmd2 = "docker compose run --rm --name chatterbox-client-bob -e CHAT_USERNAME=$($names[1]) client-spectre"
        $cmd3 = "docker compose run --rm --name chatterbox-client-charlie -e CHAT_USERNAME=$($names[2]) client-spectre"
    } else {
        $cmd1 = "docker run --rm -it --name chatterbox-client-alice --add-host=host.docker.internal:host-gateway -e HubServerUrl=$hubUrl -e OTEL_EXPORTER_OTLP_ENDPOINT=$otlpUrl -e OTEL_EXPORTER_OTLP_PROTOCOL=grpc -e OTEL_SERVICE_NAME=Client.Spectre.Alice -e CHAT_USERNAME=$($names[0]) trifold/client-spectre:latest"
        $cmd2 = "docker run --rm -it --name chatterbox-client-bob --add-host=host.docker.internal:host-gateway -e HubServerUrl=$hubUrl -e OTEL_EXPORTER_OTLP_ENDPOINT=$otlpUrl -e OTEL_EXPORTER_OTLP_PROTOCOL=grpc -e OTEL_SERVICE_NAME=Client.Spectre.Bob -e CHAT_USERNAME=$($names[1]) trifold/client-spectre:latest"
        $cmd3 = "docker run --rm -it --name chatterbox-client-charlie --add-host=host.docker.internal:host-gateway -e HubServerUrl=$hubUrl -e OTEL_EXPORTER_OTLP_ENDPOINT=$otlpUrl -e OTEL_EXPORTER_OTLP_PROTOCOL=grpc -e OTEL_SERVICE_NAME=Client.Spectre.Charlie -e CHAT_USERNAME=$($names[2]) trifold/client-spectre:latest"
    }

    $wtArgs = @(
        "-w", "0",
        "new-tab", "-d", $rootDir, "--title", "Client 1 ($($names[0]))", "pwsh", "-NoExit", "-Command", $cmd1,
        ";",
        "split-pane", "-H", "-d", $rootDir, "--title", "Client 2 ($($names[1]))", "pwsh", "-NoExit", "-Command", $cmd2,
        ";",
        "split-pane", "-V", "-d", $rootDir, "--title", "Client 3 ($($names[2]))", "pwsh", "-NoExit", "-Command", $cmd3
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new("wt.exe")
    foreach ($arg in $wtArgs) {
        $psi.ArgumentList.Add($arg)
    }
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables["Path"] = $env:Path

    [System.Diagnostics.Process]::Start($psi) | Out-Null
} else {
    $cmd1 = "`$env:CHAT_USERNAME='$($names[0])'; dotnet run --project src/Client.Spectre"
    $cmd2 = "`$env:CHAT_USERNAME='$($names[1])'; dotnet run --project src/Client.Spectre"
    $cmd3 = "`$env:CHAT_USERNAME='$($names[2])'; dotnet run --project src/Client.Spectre"

    $wtArgs = @(
        "-w", "0",
        "new-tab", "-d", $rootDir, "--title", "Client 1 ($($names[0]))", "pwsh", "-NoExit", "-Command", $cmd1,
        ";",
        "split-pane", "-H", "-d", $rootDir, "--title", "Client 2 ($($names[1]))", "pwsh", "-NoExit", "-Command", $cmd2,
        ";",
        "split-pane", "-V", "-d", $rootDir, "--title", "Client 3 ($($names[2]))", "pwsh", "-NoExit", "-Command", $cmd3
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new("wt.exe")
    foreach ($arg in $wtArgs) {
        $psi.ArgumentList.Add($arg)
    }
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables["Path"] = $env:Path

    [System.Diagnostics.Process]::Start($psi) | Out-Null
}
