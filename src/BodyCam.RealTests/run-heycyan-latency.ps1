#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run HeyCyan camera latency benchmarks on real hardware.

.DESCRIPTION
    Executes the real-hardware latency tests in BodyCam.RealTests, gating on
    BODYCAM_REAL_HEYCYAN=1 and BODYCAM_REAL_HEYCYAN_MAC environment variables.
    
    Results are written to TestResults/heycyan-latency.csv with percentiles.

.PARAMETER Mac
    The MAC address of the HeyCyan glasses to test (e.g., "A1:B2:C3:D4:E5:F6").

.EXAMPLE
    .\run-heycyan-latency.ps1 -Mac "A1:B2:C3:D4:E5:F6"
#>
param(
    [Parameter(Mandatory)]
    [string]$Mac
)

$ErrorActionPreference = 'Stop'

Write-Host "HeyCyan Latency Benchmark" -ForegroundColor Cyan
Write-Host "Target MAC: $Mac" -ForegroundColor Gray

# Set environment variables
$env:BODYCAM_REAL_HEYCYAN     = '1'
$env:BODYCAM_REAL_HEYCYAN_MAC = $Mac

# Run the latency tests
Write-Host "`nRunning HeyCyanCameraLatencyTests..." -ForegroundColor Yellow

dotnet test src\BodyCam.RealTests\BodyCam.RealTests.csproj `
    --filter "FullyQualifiedName~HeyCyanCameraLatencyTests" `
    --logger "console;verbosity=normal" `
    --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "Tests failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "`nBenchmark complete. Results written to TestResults/heycyan-latency.csv" -ForegroundColor Green
