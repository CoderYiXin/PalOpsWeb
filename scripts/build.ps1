[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$npmRegistry = 'https://registry.npmjs.org/'
$npmCache = Join-Path $root '.npm-cache'

function Assert-Command([string]$Name, [string]$InstallHint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "找不到 $Name。$InstallHint"
    }
}

function Invoke-Native([string]$Name, [string[]]$Arguments) {
    & $Name @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name 执行失败，退出码：$LASTEXITCODE"
    }
}

function Invoke-Npm([string[]]$Arguments) {
    if ($script:npmCliPath) {
        & $script:nodeExecutable $script:npmCliPath @Arguments
    }
    else {
        & $script:npmExecutable @Arguments
    }

    if ($LASTEXITCODE -ne 0) {
        throw "npm 执行失败，退出码：$LASTEXITCODE"
    }
}

Assert-Command 'dotnet' '请安装 .NET 10 SDK。'
Assert-Command 'node' '请安装 Node.js 22 LTS 或更新版本。'

$nodeCommand = Get-Command 'node' -ErrorAction Stop
$script:nodeExecutable = $nodeCommand.Source
$script:npmCliPath = Join-Path (Split-Path -Parent $script:nodeExecutable) 'node_modules\npm\bin\npm-cli.js'
$script:npmExecutable = $null

if (-not (Test-Path -LiteralPath $script:npmCliPath -PathType Leaf)) {
    $script:npmCliPath = $null
    $npmCommand = Get-Command 'npm' -ErrorAction SilentlyContinue
    if (-not $npmCommand) {
        throw '找不到 npm。请安装包含 npm 的 Node.js 22 LTS 或更新版本。'
    }

    $script:npmExecutable = $npmCommand.Source
}

Push-Location (Join-Path $root 'frontend-vue')
try {
    Invoke-Npm @('ci', "--registry=$npmRegistry", "--cache=$npmCache")
    Invoke-Npm @('run', 'typecheck')
    Invoke-Npm @('run', 'build')
    Invoke-Npm @('audit', '--audit-level=high', "--registry=$npmRegistry", "--cache=$npmCache")
}
finally {
    Pop-Location
}

Push-Location $root
try {
    $toolProject = Join-Path $root 'tools\PalOps.Tooling'
    Invoke-Native 'dotnet' @('restore', '.\PalOpsWeb.slnx')
    Invoke-Native 'dotnet' @('build', '.\PalOpsWeb.slnx', '-c', 'Release', '--no-restore')
    Invoke-Native 'dotnet' @(
        'run', '--project', $toolProject, '-c', 'Release', '--no-build', '--',
        'catalog', 'verify', '--root', $root)
    Invoke-Native 'dotnet' @(
        'run', '--project', $toolProject, '-c', 'Release', '--no-build', '--',
        'map', 'verify', '--root', $root, '--allow-missing')
    Invoke-Native 'dotnet' @(
        'run', '--project', $toolProject, '-c', 'Release', '--no-build', '--',
        'docs', 'verify', '--root', $root)
    Invoke-Native 'dotnet' @(
        'run', '--project', $toolProject, '-c', 'Release', '--no-build', '--',
        'release', 'verify', '--root', $root)
}
finally {
    Pop-Location
}

Write-Host 'PalOps Web 构建、前端、目录、文档与源代码校验完成。发布前仍需严格校验完整地图瓦片。' -ForegroundColor Green
