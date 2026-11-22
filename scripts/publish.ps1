param(
    [string]$Target = 'D:\tools\Siming'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'src\Siming.App\Siming.App.csproj'
$mcpProject = Join-Path $root 'src\Siming.Mcp\Siming.Mcp.csproj'

if (Get-Process -Name 'Siming', 'Siming.Mcp' -ErrorAction SilentlyContinue) {
    throw 'Siming or Siming.Mcp is running. Exit both before publishing.'
}

$publishArgs = @(
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none'
)

dotnet publish $appProject @publishArgs -o $Target
dotnet publish $mcpProject @publishArgs -o $Target

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

Write-Host "Published to $Target"
