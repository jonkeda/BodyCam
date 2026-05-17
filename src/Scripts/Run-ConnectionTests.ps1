<#
.SYNOPSIS
    Runs the real-hardware HeyCyan connection flow tests.

.DESCRIPTION
    Requires HeyCyan glasses powered on and discoverable.
    Set BODYCAM_REAL_HEYCYAN_MAC to your glasses' BLE address before running.

.PARAMETER Mac
    BLE MAC address of the glasses (e.g. "D8:79:B8:7F:E6:C9").
    Defaults to BODYCAM_REAL_HEYCYAN_MAC env var.

.PARAMETER Name
    Expected device name prefix (optional, e.g. "M01 Pro").

.PARAMETER Verbosity
    dotnet test verbosity. Default: normal.

.EXAMPLE
    .\Run-ConnectionTests.ps1 -Mac "D8:79:B8:7F:E6:C9"
    .\Run-ConnectionTests.ps1 -Mac "D8:79:B8:7F:E6:C9" -Name "M01 Pro" -Verbosity detailed
#>
param(
    [string]$Mac = ($env:BODYCAM_REAL_HEYCYAN_MAC ?? "D8:79:B8:7F:E6:C9"),
    [string]$Name = $env:BODYCAM_REAL_HEYCYAN_NAME,
    [ValidateSet("quiet","minimal","normal","detailed","diagnostic")]
    [string]$Verbosity = "normal"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Mac)) {
    Write-Error "MAC address required. Pass -Mac or set BODYCAM_REAL_HEYCYAN_MAC."
    exit 1
}

$env:BODYCAM_REAL_HEYCYAN = "1"
$env:BODYCAM_REAL_HEYCYAN_MAC = $Mac
if ($Name) { $env:BODYCAM_REAL_HEYCYAN_NAME = $Name }

$project = Join-Path $PSScriptRoot "..\BodyCam.RealTests\BodyCam.RealTests.csproj"

Write-Host "Running HeyCyan connection tests (MAC=$Mac)" -ForegroundColor Cyan
dotnet restore $project /p:TargetFramework=net10.0-windows10.0.19041.0 --verbosity quiet
dotnet test $project `
    -f net10.0-windows10.0.19041.0 `
    --no-restore `
    --filter "Category=RealConnection" `
    -v $Verbosity
