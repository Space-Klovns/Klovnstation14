param([switch]$SkipBuild, [switch]$SkipSpritePreviews,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$procgenRepository = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$procgenOutput = Join-Path $procgenRepository 'Klovn\Docs\Examples\Procgen\Showcases'
$procgenMaps = Join-Path $procgenRepository 'Resources\Maps\_KS14\ProcgenShowcases'
$procgenPreviousOutput = $env:KS_PROCGEN_SHOWCASE_DIR
Push-Location -LiteralPath $procgenRepository
try {
    if (-not $SkipBuild) {
        & dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj --no-restore -m:1 -nr:false -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Showcase build failed.' }
    }
    $env:KS_PROCGEN_SHOWCASE_DIR = $procgenOutput
    & dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-build --no-restore -c $Configuration --filter 'FullyQualifiedName~KsProcgenShowcaseTests' --logger 'trx;LogFileName=ks_showcases.trx'
    if ($LASTEXITCODE -ne 0) { throw 'Showcase generation or native grid validation failed.' }
    if (-not $SkipSpritePreviews) {
        & (Join-Path $PSScriptRoot 'render_sprite_showcases.ps1') -Directory $procgenOutput
    }
    & python (Join-Path $PSScriptRoot 'render_showcases.py') $procgenOutput
    if ($LASTEXITCODE -ne 0) { throw 'Showcase rendering failed.' }
    New-Item -ItemType Directory -Path $procgenMaps -Force | Out-Null
    foreach ($procgenExample in @('tiny-strip', 'l-shape', 'kitchenette', 'gas-room', 'wired-office')) {
        Copy-Item -LiteralPath (Join-Path $procgenOutput ($procgenExample + '.yml')) -Destination (Join-Path $procgenMaps ($procgenExample + '.yml'))
    }
    Write-Output ('Gallery: ' + (Join-Path $procgenOutput 'index.html'))
    Write-Output ('Native grid resources: ' + $procgenMaps)
}
finally {
    $env:KS_PROCGEN_SHOWCASE_DIR = $procgenPreviousOutput
    Pop-Location
}
