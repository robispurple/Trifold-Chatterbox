param (
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$rootDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Push-Location $rootDir

try {
    $null = dotnet csharprepl --version 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "csharprepl local tool is not restored. Run 'dotnet tool restore' first."
    }

    Write-Host "[WARNING] Launching HubServer with CSharpRepl live attach enabled (LOCAL DEVELOPMENT ONLY)." -ForegroundColor Yellow

    $initLines = dotnet csharprepl connect init --shell pwsh | Where-Object { $_ -match '^\$env:' }
    foreach ($line in $initLines) {
        Invoke-Expression $line
    }

    $env:ASPNETCORE_ENVIRONMENT = "Development"

    Write-Host ""
    Write-Host "In a second terminal (from repo root), attach with:" -ForegroundColor Cyan
    Write-Host "  dotnet csharprepl connect list" -ForegroundColor White
    Write-Host "  dotnet csharprepl connect <PID> repl/live-inspect.csx" -ForegroundColor White
    Write-Host ""

    $runArgs = @("run", "--project", "src/HubServer")
    if ($NoBuild) {
        $runArgs += "--no-build"
    }

    dotnet @runArgs
}
finally {
    Pop-Location
}
