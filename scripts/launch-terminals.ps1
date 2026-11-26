param (
    [switch]$Docker,
    [ValidateSet("Spectre", "TerminalGui", "Jumbee", "Mixed")]
    [string]$Client = "Spectre"
)

# 1. Refresh PATH in this process before spawning Windows Terminal
$machinePath = [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::Machine)
$userPath = [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::User)
$env:Path = "$userPath;$machinePath"

$rootDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$clientMetadata = @{
    Spectre = @{
        Type       = "Spectre"
        Project    = "src/Client.Spectre"
        Service    = "client-spectre"
        Image      = "trifold/client-spectre:latest"
        OtelPrefix = "Client.Spectre"
    }
    TerminalGui = @{
        Type       = "TerminalGui"
        Project    = "src/Client.TerminalGui"
        Service    = "client-terminalgui"
        Image      = "trifold/client-terminalgui:latest"
        OtelPrefix = "Client.TerminalGui"
    }
    Jumbee = @{
        Type       = "Jumbee"
        Project    = "src/Client.Jumbee"
        Service    = "client-jumbee"
        Image      = "trifold/client-jumbee:latest"
        OtelPrefix = "Client.Jumbee"
    }
}

$paneSpecs = switch ($Client) {
    "Spectre" {
        @(
            @{ Name = "Alice";   Client = $clientMetadata["Spectre"] },
            @{ Name = "Bob";     Client = $clientMetadata["Spectre"] },
            @{ Name = "Charlie"; Client = $clientMetadata["Spectre"] }
        )
    }
    "TerminalGui" {
        @(
            @{ Name = "Alice";   Client = $clientMetadata["TerminalGui"] },
            @{ Name = "Bob";     Client = $clientMetadata["TerminalGui"] },
            @{ Name = "Charlie"; Client = $clientMetadata["TerminalGui"] }
        )
    }
    "Jumbee" {
        @(
            @{ Name = "Alice";   Client = $clientMetadata["Jumbee"] },
            @{ Name = "Bob";     Client = $clientMetadata["Jumbee"] },
            @{ Name = "Charlie"; Client = $clientMetadata["Jumbee"] }
        )
    }
    "Mixed" {
        @(
            @{ Name = "Alice";   Client = $clientMetadata["Spectre"] },
            @{ Name = "Bob";     Client = $clientMetadata["TerminalGui"] },
            @{ Name = "Charlie"; Client = $clientMetadata["Jumbee"] }
        )
    }
}

$commands = @()

if ($Docker) {
    Write-Host "Cleaning up previous client containers..." -ForegroundColor Yellow
    docker ps -a -q --filter "name=chatterbox-client-" | ForEach-Object { docker rm -f $_ } 2>$null
    docker ps -a -q --filter "ancestor=trifold/client-spectre:latest" | ForEach-Object { docker rm -f $_ } 2>$null
    docker ps -a -q --filter "ancestor=trifold/client-terminalgui:latest" | ForEach-Object { docker rm -f $_ } 2>$null
    docker ps -a -q --filter "ancestor=trifold/client-jumbee:latest" | ForEach-Object { docker rm -f $_ } 2>$null

    # Check if a local HubServer / AspireHost is running on host port 5000
    $hubUrl = "http://host.docker.internal:5000"
    $grpcUrl = "http://host.docker.internal:5001"
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
            $grpcUrl = "http://hubserver:5001"
            $otlpUrl = "http://aspire-dashboard:18889"
        } else {
            Write-Host "Detected local AspireHost / HubServer on host port 5000. Connecting containers via host.docker.internal..." -ForegroundColor Green
        }
    } else {
        $hubUrl = "http://hubserver:8080"
        $grpcUrl = "http://hubserver:5001"
        $otlpUrl = "http://aspire-dashboard:18889"
    }

    foreach ($spec in $paneSpecs) {
        $name = $spec.Name
        $containerName = "chatterbox-client-$($name.ToLowerInvariant())"
        $otelName = "$($spec.Client.OtelPrefix).$name"

        if ($hubUrl -like "*hubserver:8080*") {
            $commands += "docker compose run --rm --name $containerName -e CHAT_USERNAME=$name -e OTEL_SERVICE_NAME=$otelName $($spec.Client.Service)"
        } else {
            $commands += "docker run --rm -it --name $containerName --add-host=host.docker.internal:host-gateway -e HubServerUrl=$hubUrl -e GrpcServerUrl=$grpcUrl -e OTEL_EXPORTER_OTLP_ENDPOINT=$otlpUrl -e OTEL_EXPORTER_OTLP_PROTOCOL=grpc -e OTEL_SERVICE_NAME=$otelName -e CHAT_USERNAME=$name $($spec.Client.Image)"
        }
    }
} else {
    foreach ($spec in $paneSpecs) {
        $commands += "dotnet run --no-build --project $($spec.Client.Project) -e CHAT_USERNAME=$($spec.Name)"
    }
}

$wtArgs = @(
    "-w", "0",
    "new-tab", "-d", $rootDir, "--title", "Client 1 ($($paneSpecs[0].Name) - $($paneSpecs[0].Client.Type))", "pwsh", "-NoExit", "-Command", $commands[0],
    ";",
    "split-pane", "-H", "-d", $rootDir, "--title", "Client 2 ($($paneSpecs[1].Name) - $($paneSpecs[1].Client.Type))", "pwsh", "-NoExit", "-Command", $commands[1],
    ";",
    "split-pane", "-V", "-d", $rootDir, "--title", "Client 3 ($($paneSpecs[2].Name) - $($paneSpecs[2].Client.Type))", "pwsh", "-NoExit", "-Command", $commands[2]
)

$psi = [System.Diagnostics.ProcessStartInfo]::new("wt.exe")
foreach ($arg in $wtArgs) {
    $psi.ArgumentList.Add($arg)
}
$psi.UseShellExecute = $false
$psi.EnvironmentVariables["Path"] = $env:Path

[System.Diagnostics.Process]::Start($psi) | Out-Null
