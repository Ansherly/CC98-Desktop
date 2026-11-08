<#
.SYNOPSIS
    下载 libmpv-2.dll 到仓库的 native\ 目录(该文件不进版本库)。

.DESCRIPTION
    播放器用 mpv 做解码与渲染(OpenGL → D3D11 共享纹理 → SwapChainPanel),
    运行时需要同目录下的 libmpv-2.dll。这里从 shinchiro 的 mpv-winbuild-cmake
    发行版里取 x86_64 的 dev 包并解出 libmpv-2.dll。

.EXAMPLE
    pwsh -File tools\fetch-libmpv.ps1
    pwsh -File tools\fetch-libmpv.ps1 -Force
#>
[CmdletBinding()]
param(
    [string]$Tag = '20261107',
    [string]$AssetPattern = 'mpv-dev-x86_64-*-git-*.7z',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$targetDir = Join-Path $repoRoot 'native'
$target = Join-Path $targetDir 'libmpv-2.dll'

if ((Test-Path $target) -and -not $Force) {
    Write-Host "libmpv-2.dll 已存在:$target(用 -Force 重新下载)"
    exit 0
}

New-Item -ItemType Directory -Force $targetDir | Out-Null
$work = Join-Path $env:TEMP 'cc98-libmpv'
New-Item -ItemType Directory -Force $work | Out-Null

# 7zr.exe 只能解 .7z,足够用了
$sevenZip = Join-Path $work '7zr.exe'
if (-not (Test-Path $sevenZip)) {
    Write-Host '下载 7zr.exe ...'
    Invoke-WebRequest -Uri 'https://www.7-zip.org/a/7zr.exe' -OutFile $sevenZip
}

# 从发行版页面解析出具体资源名(资源名里带 git 提交号)
Write-Host "查询 mpv 发行版 $Tag ..."
$expanded = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/expanded_assets/$Tag"
$html = (Invoke-WebRequest -Uri $expanded -Headers @{ 'User-Agent' = 'cc98-fetch' }).Content
$match = [regex]::Match($html, "/shinchiro/mpv-winbuild-cmake/releases/download/$Tag/([^`"']*x86_64[^`"']*\.7z)")
$assets = [regex]::Matches($html, "/shinchiro/mpv-winbuild-cmake/releases/download/$Tag/([^`"']*\.7z)") |
    ForEach-Object { $_.Groups[1].Value } |
    Where-Object { $_ -like $AssetPattern } |
    Select-Object -Unique

if (-not $assets) {
    throw "在 $expanded 上没找到匹配 $AssetPattern 的资源"
}

$asset = @($assets)[0]
$archive = Join-Path $work $asset
if (-not (Test-Path $archive)) {
    Write-Host "下载 $asset(约 30 MB)..."
    Invoke-WebRequest -Uri "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$Tag/$asset" -OutFile $archive
}

Write-Host '解出 libmpv-2.dll ...'
& $sevenZip e $archive "-o$targetDir" 'libmpv-2.dll' -r -y | Out-Null

if (-not (Test-Path $target)) {
    throw "解压完成但没找到 $target"
}

$size = [math]::Round((Get-Item $target).Length / 1MB, 1)
Write-Host "完成:$target($size MB)"
