param(
    [string]$Target = 'D:\tools\Siming',
    [switch]$Thin
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'src\Siming.App\Siming.App.csproj'

if (Get-Process -Name 'Siming' -ErrorAction SilentlyContinue) {
    throw 'Siming.exe 正在运行（主程序或 AI 客户端的 MCP 服务）。退出司命，并重启正在使用 MCP 的 AI 客户端后再发布。'
}

$publishArgs = @(
    '-c', 'Release',
    '-r', 'win-x64',
    '-p:PublishSingleFile=true',
    '-p:DebugType=none'
)

if ($Thin) {
    # 框架依赖：exe 约 4 MB，目标机器需要 .NET 10 Desktop Runtime。
    $publishArgs += @('--self-contained', 'false')
}
else {
    # 自包含：把 .NET 运行时打进 exe，目标机器无需安装运行时。
    $publishArgs += @(
        '--self-contained', 'true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true'
    )
}

dotnet publish $appProject @publishArgs -o $Target
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码 $LASTEXITCODE"
}

# 旧版把 MCP 服务单独发布成 Siming.Mcp.exe，现已合并到主程序。
$legacyMcp = Join-Path $Target 'Siming.Mcp.exe'
if (Test-Path $legacyMcp) {
    try {
        Remove-Item $legacyMcp -Force
        Write-Host "Removed legacy Siming.Mcp.exe"
    }
    catch {
        Write-Warning "旧版 Siming.Mcp.exe 正在使用中，未能删除：$legacyMcp"
    }
}

$config = Join-Path $Target 'processes.json'
if (-not (Test-Path $config)) {
    $debugConfig = Join-Path $root 'src\Siming.App\bin\Debug\net10.0-windows\processes.json'
    $sample = Join-Path $root 'processes.sample.json'

    if (Test-Path $debugConfig) {
        Copy-Item $debugConfig $config
        Write-Host "Seeded config from Debug build: $config"
    }
    elseif (Test-Path $sample) {
        Copy-Item $sample $config
        Write-Host "Seeded config from sample: $config"
    }
}

$mode = if ($Thin) { 'framework-dependent' } else { 'self-contained' }
Write-Host "Published Siming.exe to $Target ($mode)"
