[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$toolProject = Join-Path $root 'tools\PalOps.Tooling'
$artifactsDirectory = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$releaseBaseName = "palops-web-$Version-win-x64"
$OutputDirectory = Join-Path $artifactsDirectory $releaseBaseName
$zipPath = Join-Path $artifactsDirectory "$releaseBaseName.zip"
$zipHashPath = Join-Path $artifactsDirectory "$releaseBaseName.sha256"
$verificationRoot = Join-Path $artifactsDirectory "$releaseBaseName-verify"

function Invoke-Native([string]$Name, [string[]]$Arguments) {
    & $Name @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name 执行失败，退出码：$LASTEXITCODE"
    }
}

function Assert-NoReparsePoint([string]$Path, [switch]$Recursive) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "拒绝使用符号链接或目录联接路径：$Path"
    }

    if ($Recursive) {
        $nested = Get-ChildItem -LiteralPath $Path -Force -Recurse |
            Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 } |
            Select-Object -First 1
        if ($nested) {
            throw "目录包含符号链接或目录联接：$($nested.FullName)"
        }
    }
}

New-Item -Path $artifactsDirectory -ItemType Directory -Force | Out-Null
Assert-NoReparsePoint -Path $artifactsDirectory

& (Join-Path $PSScriptRoot 'build.ps1')
Invoke-Native 'dotnet' @(
    'run', '--project', $toolProject, '-c', 'Release', '--no-build', '--',
    'map', 'verify', '--root', $root,
    '--require-layers', 'palpagos,world-tree')
Invoke-Native 'dotnet' @(
    'run', '--project', $toolProject, '-c', 'Release', '--no-build', '--',
    'release', 'verify', '--root', $root)

foreach ($path in @($OutputDirectory, $verificationRoot)) {
    if (-not (Test-Path -LiteralPath $path)) {
        continue
    }
    Assert-NoReparsePoint -Path $path -Recursive
    Remove-Item -LiteralPath $path -Recurse -Force
}
foreach ($path in @($zipPath, $zipHashPath)) {
    if (Test-Path -LiteralPath $path) {
        Assert-NoReparsePoint -Path $path
        Remove-Item -LiteralPath $path -Force
    }
}

New-Item -Path $OutputDirectory -ItemType Directory -Force | Out-Null

$webProject = Join-Path $root 'src\PalOps.Web\PalOps.Web.csproj'
Invoke-Native 'dotnet' @('restore', $webProject, '-r', 'win-x64')

$publishArgs = @(
    'publish',
    $webProject,
    '-c', 'Release',
    '-r', 'win-x64',
    '--no-restore',
    '-o', $OutputDirectory,
    '-p:PublishSingleFile=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:Version=' + $Version,
    '-p:InformationalVersion=' + $Version
)

if ($SelfContained) {
    $publishArgs += '--self-contained'
    $publishArgs += 'true'
}
else {
    $publishArgs += '--self-contained'
    $publishArgs += 'false'
}

Invoke-Native 'dotnet' $publishArgs
Copy-Item (Join-Path $PSScriptRoot 'start.cmd') (Join-Path $OutputDirectory 'start.cmd') -Force

foreach ($file in @(
    'README.md',
    'README.en.md',
    'CHANGELOG.md',
    'LICENSE',
    'NOTICE',
    'THIRD-PARTY-NOTICES.md',
    'SECURITY.md')) {
    Copy-Item (Join-Path $root $file) (Join-Path $OutputDirectory $file) -Force
}

$publishedDocs = Join-Path $OutputDirectory 'docs'
New-Item -Path $publishedDocs -ItemType Directory -Force | Out-Null
foreach ($file in @('architecture.md', 'build.md', 'deployment.md', 'release-checklist.md')) {
    Copy-Item (Join-Path $root "docs\$file") (Join-Path $publishedDocs $file) -Force
}

Copy-Item (Join-Path $root 'licenses') (Join-Path $OutputDirectory 'licenses') -Recurse -Force

Invoke-Native 'dotnet' @(
    'run', '--project', $toolProject, '-c', 'Release', '--no-build', '--',
    'release', 'manifest',
    '--root', $OutputDirectory,
    '--output', (Join-Path $OutputDirectory 'SHA256SUMS.txt'))

Invoke-Native 'dotnet' @(
    'run', '--project', $toolProject, '-c', 'Release', '--no-build', '--',
    'map', 'verify', '--root', $root,
    '--tiles-root', (Join-Path $OutputDirectory 'wwwroot\map\tiles'),
    '--require-layers', 'palpagos,world-tree')
Invoke-Native 'dotnet' @(
    'run', '--project', $toolProject, '-c', 'Release', '--no-build', '--',
    'release', 'verify', '--root', $OutputDirectory, '--strict-tree')

Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([System.IO.Path]::GetFileName($zipPath))" |
    Set-Content -LiteralPath $zipHashPath -Encoding ascii

Expand-Archive -LiteralPath $zipPath -DestinationPath $verificationRoot
Invoke-Native 'dotnet' @(
    'run', '--project', $toolProject, '-c', 'Release', '--no-build', '--',
    'release', 'verify', '--root', $verificationRoot, '--strict-tree')
Remove-Item -LiteralPath $verificationRoot -Recurse -Force

Write-Host "发布目录：$OutputDirectory" -ForegroundColor Green
Write-Host "发布压缩包：$zipPath" -ForegroundColor Green
Write-Host "压缩包校验：$zipHashPath" -ForegroundColor Green
