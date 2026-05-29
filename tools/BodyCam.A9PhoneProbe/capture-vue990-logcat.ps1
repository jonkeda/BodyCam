param(
    [int]$DurationSeconds = 90,
    [string]$OutputPath = "",
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$captureRoot = Join-Path $repoRoot ".my\plan\m38-a9-camera\captures"
if (-not (Test-Path $captureRoot)) {
    New-Item -ItemType Directory -Path $captureRoot | Out-Null
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
    $OutputPath = Join-Path $captureRoot "vue990-logcat-$stamp.txt"
}

$deviceLines = & adb devices | Select-String -Pattern "device$"
if (-not $deviceLines) {
    throw "No authorized ADB device is connected."
}

$patterns = @(
    "com.langxing.zhilianjiumu",
    "Vue990",
    "vstarcam",
    "veepai",
    "OKSMART",
    "PPCS",
    "XQP2P",
    "HLP2P",
    "VEEPAI",
    "JNIApi",
    "writeCgi",
    "clientSetVuid",
    "get_status",
    "192\.168\.168",
    "vuid",
    "did",
    "P2P"
)

$patternText = ($patterns -join "|")
$rawLog = Join-Path ([System.IO.Path]::GetTempPath()) ("vue990-logcat-raw-" + [System.Guid]::NewGuid().ToString("N") + ".txt")
$errLog = Join-Path ([System.IO.Path]::GetTempPath()) ("vue990-logcat-err-" + [System.Guid]::NewGuid().ToString("N") + ".txt")

& adb logcat -c | Out-Null

if (-not $NoLaunch) {
    & adb shell monkey -p com.langxing.zhilianjiumu 1 | Out-Null
    Start-Sleep -Seconds 2
}

$appPid = (& adb shell pidof -s com.langxing.zhilianjiumu).Trim()
if ([string]::IsNullOrWhiteSpace($appPid)) {
    Write-Warning "Vue990 process is not running; falling back to filtered device logcat."
    $logcatArgs = @("logcat", "-v", "time")
}
else {
    $logcatArgs = @("logcat", "--pid=$appPid", "-v", "time")
}

$process = Start-Process `
    -FilePath "adb" `
    -ArgumentList $logcatArgs `
    -RedirectStandardOutput $rawLog `
    -RedirectStandardError $errLog `
    -WindowStyle Hidden `
    -PassThru

try {
    Start-Sleep -Seconds $DurationSeconds
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
}

$filteredLines = @(
    "# Vue990 focused logcat"
    "# Captured: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz")"
    "# DurationSeconds: $DurationSeconds"
    "# Package: com.langxing.zhilianjiumu"
    "# AppPid: $appPid"
    ""
)

$filteredLines += Get-Content -Path $rawLog -ErrorAction SilentlyContinue |
    Select-String -Pattern $patternText |
    ForEach-Object { $_.Line }

$filteredLines | Set-Content -Path $OutputPath -Encoding UTF8

Remove-Item -LiteralPath $rawLog -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $errLog -ErrorAction SilentlyContinue

Write-Host "Saved focused Vue990 logcat to $OutputPath"
