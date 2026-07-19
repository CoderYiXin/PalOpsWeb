[CmdletBinding()]
param(
    [int]$Port = 5178,
    [string]$RuleName = 'PalOps Web LAN'
)

$ErrorActionPreference = 'Stop'
if ($Port -lt 1 -or $Port -gt 65535) {
    throw '端口必须在 1 到 65535 之间。'
}

$existing = Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue
if ($existing) {
    Remove-NetFirewallRule -DisplayName $RuleName
}

New-NetFirewallRule `
    -DisplayName $RuleName `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort $Port `
    -RemoteAddress LocalSubnet `
    -Profile Private | Out-Null

Write-Host "已允许本地子网访问 TCP $Port。" -ForegroundColor Green
