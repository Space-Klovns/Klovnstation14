param([string]$Directory)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$procgenRepository = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$procgenTextures = Join-Path $procgenRepository 'Resources\Textures'
if (-not $Directory) { $Directory = Join-Path $procgenRepository 'Klovn\Docs\Examples\Procgen\Showcases' }
$Directory = (Resolve-Path -LiteralPath $Directory).Path
$procgenImages = @{}
$procgenMetadata = @{}
$procgenSources = @{}
$procgenOutputs = @()

function Get-Texture([string]$relative) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $procgenTextures $relative))
    if (-not $absolute.StartsWith($procgenTextures + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Texture escapes resource directory.' }
    if (-not $procgenImages.ContainsKey($relative)) {
        $procgenImages[$relative] = [Drawing.Image]::FromFile($absolute)
        $procgenSources[$relative] = @{ path = ('Resources/Textures/' + $relative.Replace('\', '/')); sha256 = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    return $procgenImages[$relative]
}

function Get-Rsi([string]$relative) {
    if (-not $procgenMetadata.ContainsKey($relative)) {
        $path = Join-Path $procgenTextures ($relative + '/meta.json')
        $procgenMetadata[$relative] = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        $procgenSources[$relative + '/meta.json'] = @{ path = ('Resources/Textures/' + $relative + '/meta.json');
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();
            license = $procgenMetadata[$relative].license; copyright = $procgenMetadata[$relative].copyright }
    }
    return $procgenMetadata[$relative]
}

function Draw-Rsi($graphics, [string]$rsi, [string]$state, [double]$x, [double]$y, [int]$turn = 0, [string]$tint = 'White', [bool]$rotateSingle = $true) {
    $meta = Get-Rsi $rsi
    $definition = @($meta.states | Where-Object { $_.name -eq $state })
    if ($definition.Count -ne 1) { throw ('Missing RSI state: ' + $rsi + '/' + $state) }
    $directions = $definition[0].directions
    if (-not $directions) { $directions = 1 }
    if ($directions -ne 1 -and $directions -ne 4) { throw 'Showcase renderer supports single/cardinal RSI states.' }
    $direction = 0
    if ($directions -eq 4) { $direction = @(0, 3, 1, 2)[$turn % 4] }
    $frameIndex = 0
    for ($index = 0; $index -lt $direction; $index++) {
        if ($definition[0].delays) { $frameIndex += @($definition[0].delays[$index]).Count } else { $frameIndex++ }
    }
    $image = Get-Texture ($rsi + '/' + $state + '.png')
    $width = [int]$meta.size.x; $height = [int]$meta.size.y
    $columns = [int]($image.Width / $width)
    $frame = New-Object Drawing.Bitmap $width, $height
    $frameGraphics = [Drawing.Graphics]::FromImage($frame)
    try {
        $frameGraphics.DrawImage($image, [Drawing.Rectangle]::new(0, 0, $width, $height),
            ($frameIndex % $columns) * $width, [int][Math]::Floor($frameIndex / $columns) * $height, $width, $height, [Drawing.GraphicsUnit]::Pixel)
    } finally { $frameGraphics.Dispose() }
    $attributes = New-Object Drawing.Imaging.ImageAttributes
    try {
        if ($directions -eq 1 -and $rotateSingle) { $frame.RotateFlip([Drawing.RotateFlipType]($turn % 4)) }
        $color = [Drawing.Color]::FromName($tint)
        $matrix = New-Object Drawing.Imaging.ColorMatrix
        $matrix.Matrix00 = $color.R / 255.0; $matrix.Matrix11 = $color.G / 255.0; $matrix.Matrix22 = $color.B / 255.0
        $attributes.SetColorMatrix($matrix)
        $destination = [Drawing.Rectangle]::new([int][Math]::Round($x - $frame.Width / 2.0), [int][Math]::Round($y - $frame.Height / 2.0), $frame.Width, $frame.Height)
        $graphics.DrawImage($frame, $destination, 0, 0, $frame.Width, $frame.Height, [Drawing.GraphicsUnit]::Pixel, $attributes)
    } finally { $attributes.Dispose(); $frame.Dispose() }
}

function Draw-Corners($graphics, [string]$rsi, [string]$prefix, [int]$x, [int]$y, $occupied, [double]$centerX, [double]$centerY) {
    $north = [int]$occupied.ContainsKey("$x,$($y + 1)"); $south = [int]$occupied.ContainsKey("$x,$($y - 1)")
    $east = [int]$occupied.ContainsKey("$($x + 1),$y"); $west = [int]$occupied.ContainsKey("$($x - 1),$y")
    $ne = $north + 4 * $east + 2 * [int]$occupied.ContainsKey("$($x + 1),$($y + 1)")
    $nw = 4 * $north + $west + 2 * [int]$occupied.ContainsKey("$($x - 1),$($y + 1)")
    $sw = $south + 4 * $west + 2 * [int]$occupied.ContainsKey("$($x - 1),$($y - 1)")
    $se = 4 * $south + $east + 2 * [int]$occupied.ContainsKey("$($x + 1),$($y - 1)")
    # Native corner layers: SE/South, NE/East, NW/North, SW/West. RSI order is S,N,E,W.
    Draw-Rsi $graphics $rsi ($prefix + $se) $centerX $centerY 0
    Draw-Rsi $graphics $rsi ($prefix + $ne) $centerX $centerY 3
    Draw-Rsi $graphics $rsi ($prefix + $nw) $centerX $centerY 2
    Draw-Rsi $graphics $rsi ($prefix + $sw) $centerX $centerY 1
}

function Draw-Floor($graphics, [string]$id, [int]$x, [int]$y) {
    $path = switch ($id) {
        'FloorSteel' { '_KS14/Tiles/_AUTOGEN/floor.png' }
        'FloorSteelMini' { '_KS14/Tiles/_AUTOGEN/small.png' }
        'FloorSteelCheckerLight' { '_KS14/Tiles/_AUTOGEN/cafeteria.png' }
        'Plating' { 'Tiles/plating.png' }
        default { throw ('No showcase tile mapping: ' + $id) }
    }
    $graphics.DrawImage((Get-Texture $path), [Drawing.Rectangle]::new($x, $y, 32, 32), 0, 0, 32, 32, [Drawing.GraphicsUnit]::Pixel)
}

try {
    foreach ($name in @('tiny-strip', 'l-shape', 'kitchenette', 'gas-room', 'wired-office')) {
        $data = Get-Content -LiteralPath (Join-Path $Directory ($name + '.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
        $cells = @($data.floors) + @($data.walls) + @($data.windows) + @($data.doors)
        $left = ($cells.cell.x | Measure-Object -Minimum).Minimum; $right = ($cells.cell.x | Measure-Object -Maximum).Maximum
        $bottom = ($cells.cell.y | Measure-Object -Minimum).Minimum; $top = ($cells.cell.y | Measure-Object -Maximum).Maximum
        $walls = @{}; foreach ($item in $data.walls) { $walls["$($item.cell.x),$($item.cell.y)"] = $true }
        $windows = @{}; foreach ($item in $data.windows) { $windows["$($item.cell.x),$($item.cell.y)"] = $true }
        $tables = @{}; foreach ($item in $data.entities) { if ($item.prototypeId -eq 'Table' -and $item.exposed) { $tables["$([int][Math]::Floor($item.x)),$([int][Math]::Floor($item.y))"] = $true } }
        foreach ($wiring in @($false, $true)) {
            if ($wiring -and $name -ne 'wired-office') { continue }
            $bitmap = New-Object Drawing.Bitmap ([int](($right - $left + 1) * 32 + 32)), ([int](($top - $bottom + 1) * 32 + 32))
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::FromArgb(11, 15, 23))
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
                foreach ($item in $cells) {
                    $tile = 'FloorSteel'; if ($item.tileId) { $tile = $item.tileId }
                    if ($wiring) { $tile = 'Plating' }
                    Draw-Floor $graphics $tile ([int](16 + ($item.cell.x - $left) * 32)) ([int](16 + ($top - $item.cell.y) * 32))
                }
                foreach ($item in @($data.walls) + @($data.windows)) {
                    $x = [int]$item.cell.x; $y = [int]$item.cell.y
                    $centerX = 16 + ($x - $left + 0.5) * 32; $centerY = 16 + ($top - $y + 0.5) * 32
                    switch ($item.entityId) {
                        'WallSolid' { Draw-Corners $graphics 'Structures/Walls/solid.rsi' 'solid' $x $y $walls $centerX $centerY }
                        'WallReinforced' { Draw-Corners $graphics 'Structures/Walls/solid.rsi' 'reinf_over' $x $y $walls $centerX $centerY }
                        'Window' { Draw-Corners $graphics 'Structures/Windows/window.rsi' 'window' $x $y $windows $centerX $centerY }
                        default { throw ('No showcase structure mapping: ' + $item.entityId) }
                    }
                }
                foreach ($item in $data.doors) {
                    if ($item.entityId -ne 'Airlock') { throw ('No showcase door mapping: ' + $item.entityId) }
                    $centerX = 16 + ($item.cell.x - $left + 0.5) * 32; $centerY = 16 + ($top - $item.cell.y + 0.5) * 32
                    Draw-Rsi $graphics 'Structures/Doors/Airlocks/Standard/basic.rsi' 'closed' $centerX $centerY ([int]$item.quarterTurns)
                }
                foreach ($item in $data.lights) {
                    if ($item.entityId -ne 'LightPostSmall') { throw ('No showcase light mapping: ' + $item.entityId) }
                    $centerX = 16 + ($item.cell.x - $left + 0.5) * 32; $centerY = 16 + ($top - $item.cell.y + 0.5) * 32
                    foreach ($state in @('base', 'glow')) { Draw-Rsi $graphics 'Structures/Lighting/LightPosts/small_light_post.rsi' $state $centerX $centerY -rotateSingle $false }
                }
                foreach ($item in @($data.entities | Sort-Object @{Expression={if ($_.prototypeId -eq 'Table') { 0 } else { 1 }}}, @{Expression={-$_.y}})) {
                    if (-not $item.exposed -or $item.layer -eq 'Container') { continue }
                    $centerX = 16 + ($item.x - $left) * 32; $centerY = 16 + ($top + 1 - $item.y) * 32
                    $turn = [int]$item.quarterTurns
                    switch ($item.prototypeId) {
                        'Table' { Draw-Corners $graphics 'Structures/Furniture/Tables/generic.rsi' 'state_' ([int][Math]::Floor($item.x)) ([int][Math]::Floor($item.y)) $tables $centerX $centerY }
                        'KitchenMicrowave' { Draw-Rsi $graphics 'Structures/Machines/microwave.rsi' 'mw0' $centerX ($centerY - 6.4) $turn }
                        'KsProcgenFilteredComputerFixture' { foreach ($state in @('computer','generic_keyboard','generic_keys')) { Draw-Rsi $graphics 'Structures/Machines/computers.rsi' $state $centerX $centerY $turn } }
                        'APCBasic' { Draw-Rsi $graphics 'Structures/Power/apc.rsi' 'base' $centerX $centerY $turn; Draw-Rsi $graphics 'Structures/Power/apc.rsi' 'display-full' $centerX $centerY $turn }
                        'SubstationBasic' { foreach ($state in @('substation','screen','full')) { Draw-Rsi $graphics 'Structures/Power/substation.rsi' $state $centerX $centerY $turn } }
                        'CableHVStack10' { Draw-Rsi $graphics 'Objects/Tools/cable-coils.rsi' 'coilhv-10' $centerX $centerY $turn }
                        'CableMVStack10' { Draw-Rsi $graphics 'Objects/Tools/cable-coils.rsi' 'coilmv-10' $centerX $centerY $turn }
                        'CableApcStack10' { Draw-Rsi $graphics 'Objects/Tools/cable-coils.rsi' 'coillv-10' $centerX $centerY $turn }
                        default { throw ('No showcase entity mapping: ' + $item.prototypeId) }
                    }
                }
                # Diagnostic overlay: expose installed cables above every floor, structure and furnishing.
                if ($wiring) {
                    foreach ($cable in $data.cables) {
                        $connected = @{}; foreach ($other in $data.cables) { if ($other.voltage -eq $cable.voltage) { $connected["$($other.cell.x),$($other.cell.y)"] = $true } }
                        $x = [int]$cable.cell.x; $y = [int]$cable.cell.y
                        $mask = [int]$connected.ContainsKey("$x,$($y + 1)") + 2 * [int]$connected.ContainsKey("$x,$($y - 1)") +
                            4 * [int]$connected.ContainsKey("$($x + 1),$y") + 8 * [int]$connected.ContainsKey("$($x - 1),$y")
                        $centerX = 16 + ($x - $left + 0.5) * 32; $centerY = 16 + ($top - $y + 0.5) * 32
                        switch ($cable.voltage) {
                            'High' { Draw-Rsi $graphics 'Structures/Power/Cables/hv_cable.rsi' ("hvcable_$mask") $centerX $centerY }
                            'Medium' { Draw-Rsi $graphics 'Structures/Power/Cables/mv_cable.rsi' ("mvcable_$mask") $centerX $centerY 0 'Yellow'; Draw-Rsi $graphics 'Structures/Power/Cables/mv_cable.rsi' ("mvstripes_$mask") $centerX $centerY }
                            'Low' { Draw-Rsi $graphics 'Structures/Power/Cables/lv_cable.rsi' ("lvcable_$mask") $centerX $centerY 0 'Green' }
                            default { throw 'Unknown cable tier.' }
                        }
                    }
                }
                $suffix = '-sprites.png'; if ($wiring) { $suffix = '-wiring.png' }
                $output = Join-Path $Directory ($name + $suffix)
                $scaled = New-Object Drawing.Bitmap ($bitmap.Width * 3), ($bitmap.Height * 3)
                $scaledGraphics = [Drawing.Graphics]::FromImage($scaled)
                try {
                    $scaledGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                    $scaledGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
                    $scaledGraphics.DrawImage($bitmap, [Drawing.Rectangle]::new(0, 0, $scaled.Width, $scaled.Height), 0, 0, $bitmap.Width, $bitmap.Height, [Drawing.GraphicsUnit]::Pixel)
                    $scaled.Save($output, [Drawing.Imaging.ImageFormat]::Png)
                    $procgenOutputs += @{ path = [IO.Path]::GetFileName($output); width = $scaled.Width; height = $scaled.Height; sourcePlanHash = $data.planHash;
                        sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant() }
                    Write-Output ('Rendered repository sprites: ' + $output)
                } finally { $scaledGraphics.Dispose(); $scaled.Dispose() }
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    }
    $manifest = @{ kind = 'KsProcgenShowcaseSpriteAssets'; schemaVersion = 1; clientScreenshot = $false;
        method = 'Repository PNG/RSI frames, native cardinal/corner masks, nearest-neighbor 3x scaling; curated mappings for these five fixtures.';
        limitations = @('Full-bright static frame, without native lighting/shaders or client runtime appearance verification.',
            'Computer screen is unpowered; APC/substation use full-battery display assets, not a claim of operating supply.',
            'Normal previews hide under-floor cables. Wiring preview draws cables last above all floors, structures and furnishings.',
            'Floor variant zero and animation frame zero; contained items are not drawn.');
        sources = @($procgenSources.Values | Sort-Object path); outputs = $procgenOutputs }
    [IO.File]::WriteAllText((Join-Path $Directory 'sprite-assets.json'), ($manifest | ConvertTo-Json -Depth 12) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
} finally { foreach ($image in $procgenImages.Values) { $image.Dispose() } }
