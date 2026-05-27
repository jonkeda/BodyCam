<#
.SYNOPSIS
    Sets the FIGMA_API_KEY environment variable for the current user.

.DESCRIPTION
    Stores your Figma Personal Access Token as a persistent user-level
    environment variable so the Framelink MCP server can pick it up.

.PARAMETER Key
    Your Figma Personal Access Token. If omitted, you will be prompted.

.EXAMPLE
    .\Set-FigmaApiKey.ps1
    .\Set-FigmaApiKey.ps1 -Key "figd_xxxxx"
#>
param(
    [string]$Key
)

if (-not $Key) {
    $secure = Read-Host "Enter your Figma Personal Access Token" -AsSecureString
    $Key = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    )
}

if (-not $Key) {
    Write-Error "No key provided. Aborting."
    exit 1
}

[System.Environment]::SetEnvironmentVariable("FIGMA_API_KEY", $Key, "User")
$env:FIGMA_API_KEY = $Key

Write-Host "FIGMA_API_KEY set for the current user. Restart VS Code to pick it up." -ForegroundColor Green
