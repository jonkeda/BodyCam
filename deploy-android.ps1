#!/usr/bin/env pwsh
# Build and deploy BodyCam to a connected Android device.

$ErrorActionPreference = 'Stop'

# Verify a device is connected
$devices = adb devices | Select-String -Pattern '^\S+\s+device$'
if (-not $devices) {
    Write-Error 'No Android device connected. Connect a device and enable USB debugging.'
    exit 1
}

Write-Host "Device(s) found:" -ForegroundColor Green
$devices | ForEach-Object { Write-Host "  $_" }
Write-Host ""

Write-Host "Building and deploying BodyCam..." -ForegroundColor Cyan
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android -t:Install

if ($LASTEXITCODE -ne 0) {
    Write-Error 'Build/deploy failed.'
    exit $LASTEXITCODE
}

Write-Host "`nDeployed successfully." -ForegroundColor Green
