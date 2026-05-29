#!/usr/bin/env pwsh

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'BodyCam.A9PhoneProbe.csproj'
$apk = Join-Path $PSScriptRoot 'bin/Debug/net10.0-android/com.bodycam.a9phoneprobe-Signed.apk'

dotnet build $project -f net10.0-android -p:SkipBuildNumberIncrement=true

$devices = adb devices | Select-String -Pattern '^\S+\s+device$'
if (-not $devices) {
    Write-Error 'No Android device connected. Connect the Samsung phone, enable USB debugging, and accept the authorization prompt.'
    exit 1
}

adb install -r $apk

Write-Host 'Installed A9 Phone Probe.' -ForegroundColor Green
