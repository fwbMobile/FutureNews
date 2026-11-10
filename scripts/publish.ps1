<#
.SYNOPSIS
    发布 FutureNews 桌面程序到 publish\ 目录。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\publish.ps1

.EXAMPLE
    # 单文件、自带运行时（体积大，但目标机器无需安装 .NET）
    powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -SelfContained -SingleFile -Zip
#>
param(
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [switch]$SingleFile,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\FutureNews.App\FutureNews.App.csproj"
$outDir = Join-Path $root "publish\FutureNews-$Runtime"

# Prefer the machine-wide SDK but fall back to whatever "dotnet" is on PATH.
$dotnet = "dotnet"
if (Test-Path "C:\Program Files\dotnet\dotnet.exe") {
    $dotnet = "C:\Program Files\dotnet\dotnet.exe"
}

Write-Host "清理 $outDir" -ForegroundColor Cyan
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", $(if ($SelfContained) { "true" } else { "false" }),
    "-p:PublishSingleFile=$($SingleFile.IsPresent.ToString().ToLowerInvariant())",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $outDir
)

if ($SingleFile) {
    $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    $publishArgs += "-p:EnableCompressionInSingleFile=true"
}

Write-Host "dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray
& $dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "发布失败（退出码 $LASTEXITCODE）" }

# 附带说明文件，方便直接拷贝给他人
Copy-Item (Join-Path $root "README.md") $outDir -ErrorAction SilentlyContinue

$exe = Join-Path $outDir "FutureNews.exe"
if (-not (Test-Path $exe)) { throw "没有找到 $exe" }

$size = (Get-ChildItem $outDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("完成：{0}（{1:N1} MB）" -f $exe, $size) -ForegroundColor Green

if ($Zip) {
    $zipPath = "$outDir.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath
    Write-Host "已打包：$zipPath" -ForegroundColor Green
}
