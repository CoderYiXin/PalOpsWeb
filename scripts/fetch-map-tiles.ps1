[CmdletBinding()]
param(
    [ValidateSet('all', 'palpagos', 'world-tree')]
    [string]$Layer = 'all',
    [switch]$Force,
    [ValidateRange(1, 32)]
    [int]$MaxConcurrency = 8,
    [ValidateRange(5, 300)]
    [int]$TimeoutSeconds = 30,
    [ValidateRange(0, 10)]
    [int]$Retries = 3
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$arguments = @(
    'run',
    '--project', (Join-Path $root 'tools\PalOps.Tooling'),
    '--',
    'map', 'fetch',
    '--root', $root,
    '--layer', $Layer,
    '--max-concurrency', $MaxConcurrency,
    '--timeout-seconds', $TimeoutSeconds,
    '--retries', $Retries
)
if ($Force) {
    $arguments += '--force'
}

dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "地图资源获取失败，退出码：$LASTEXITCODE"
}
