param([switch]$SkipBuild,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$procgenRepository = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$procgenOutput = Join-Path $procgenRepository 'Klovn\Docs\Examples\Procgen\Showcases'
$procgenMaps = Join-Path $procgenRepository 'Resources\Maps\_KS14\ProcgenShowcases'
$procgenPreviousOutput = $env:KS_PROCGEN_SHOWCASE_DIR
Push-Location -LiteralPath $procgenRepository
try {
    if (-not $SkipBuild) {
        & dotnet build Content.MapRenderer/Content.MapRenderer.csproj --no-restore -m:1 -nr:false -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Native renderer/generator build failed.' }
    }
    $env:KS_PROCGEN_SHOWCASE_DIR = $procgenOutput
    & dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-build --no-restore -c $Configuration --filter 'FullyQualifiedName~KsProcgenShowcaseTests' --logger 'trx;LogFileName=ks_showcases.trx'
    if ($LASTEXITCODE -ne 0) { throw 'Native map generation or roundtrip validation failed.' }
    New-Item -ItemType Directory -Path $procgenMaps -Force | Out-Null
    $procgenMapFiles = @()
    foreach ($procgenExample in @('tiny-strip', 'l-shape', 'kitchenette', 'gas-room', 'wired-office')) {
        $procgenMapFile = Join-Path $procgenMaps ($procgenExample + '.yml')
        Copy-Item -LiteralPath (Join-Path $procgenOutput ($procgenExample + '.yml')) -Destination $procgenMapFile
        $procgenMapFiles += $procgenMapFile
    }
    & dotnet (Join-Path $procgenRepository 'bin\Content.MapRenderer\Content.MapRenderer.dll') --files @procgenMapFiles --wires-last --output (Join-Path $procgenOutput 'Native')
    if ($LASTEXITCODE -ne 0) { throw 'Native map rendering failed.' }
    Write-Output ('Native map images: ' + (Join-Path $procgenOutput 'Native'))
}
finally {
    $env:KS_PROCGEN_SHOWCASE_DIR = $procgenPreviousOutput
    Pop-Location
}
