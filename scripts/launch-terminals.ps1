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
    Write-Host "Starting Docker HubServer backend..." -ForegroundColor Cyan
    docker compose -f "$rootDir\docker-compose.yml" up -d hubserver

    $cmd1 = "docker compose run --rm -e CHAT_USERNAME=$($names[0]) client-spectre"
    $cmd2 = "docker compose run --rm -e CHAT_USERNAME=$($names[1]) client-spectre"
    $cmd3 = "docker compose run --rm -e CHAT_USERNAME=$($names[2]) client-spectre"

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
