# KS14: added in this fork
# Procedural generation: a practical guide

This guide covers the dungeon generation system in this repository. It is aimed at someone
building rooms and layouts for the first time, with minimal algorithms and examples to copy.

## The simple mental model

Think of a generated map as one tile grid plus several entity layers placed on top:

```text
tiles:       floor floor floor floor floor
             floor floor wall  floor floor
             floor floor floor floor floor

entities:    .     .     table .     .
             .     lamp  .     lamp  .
             .     .     crate .     .
```

The tile grid describes walkable shape and floor material. Entity layers add walls, doors, props,
loot, lights, and creatures. Decals and wall mounts can be thought of as more overlays. Keeping
these jobs separate makes a layout much easier to change: changing the theme usually means swapping
tile/entity choices while keeping the shape algorithm.

That model is for planning. This codebase does not currently take a hand-written `Tile[,]` or a
stack of entity arrays from YAML. Authored rooms are map files; procedural configs are ordered
`IDunGenLayer` lists. Existing generators write tiles or spawn entities themselves. A new general
array-based generator would need code support as well as a prototype format.

## Choose an approach

| Goal | Start here | How variation works |
| --- | --- | --- |
| Rooms with intentional furniture and wall details | Prefab rooms | Make several room maps with the same dimensions; the generator picks and rotates them. |
| Rooms connected into a dungeon | Prefab generation plus corridors | Room packs and presets pick the rough layout; `CorridorDunGen` connects room entrances. |
| Irregular cave or asteroid shapes | `NoiseDunGen` | Noise thresholds decide which floor tiles are written. |
| Scatter entities on generated ground | Entity tables, biome layers, or `FillGridDunGen` | Random tables or tile masks control what appears and where. |
| Exact room shape from a tiny text grid | Hand-author a map prefab | Use an ASCII sketch as a blueprint, then draw it in the map editor. |

For a first feature, prefer prefab rooms. You get room variation and hand control without writing
new generator code.

## The main prototype pieces

Dungeon prototypes live under `Resources/Prototypes/Procedural/` (Klovnstation additions should
live under `Resources/Prototypes/_KS14/Procedural/`).

1. **Room map**: a map file containing floor tiles and any intentionally placed entities.
2. **`dungeonRoom`**: identifies a rectangular slice of that map, its size, and tags.
3. **`dungeonRoomPack`**: describes room rectangles that fit into one larger pack size.
4. **`dungeonPreset`**: places room packs relative to one another to form the overall outline.
5. **`dungeonConfig`**: runs generators in order: make the floor/rooms, connect them, then decorate.

See `Resources/Prototypes/Procedural/Maps/bagel.yml` for room prototype syntax and
`Resources/Prototypes/Procedural/dungeon_configs.yml` for complete themes. `Experiment`, `Haunted`,
`Mineshaft`, and `SnowyLabs` are useful references. `Resources/Prototypes/Procedural/dungeon_presets.yml`
and `dungeon_room_packs.yml` show how preset geometry is defined.

## A room, from sketch to prototype

Sketch a compact room using one character per tile. For example, `#` is wall, `.` is floor, `D` is
a doorway, and letters are props:

```text
#########
#.......#
#..T....#
D...L...D
#.......#
#.......#
#########
```

This sketch is documentation for you; it is not parsed by the game. Draw the equivalent in a map
editor and save it in an atlas map file. Build the room as a normal map: set its floor, then place
the wall entities, doors, and props. Keep room dimensions consistent across variants that should
substitute for each other. Give door openings the same edge positions so corridors can connect.

Register a rectangular section of the atlas:

```yaml
- type: dungeonRoom
  id: KsRuinedStoreRoomA
  size: 9, 7
  atlas: /Maps/_KS14/Dungeon/ruined_station.yml
  offset: 0, 0
  tags:
  - KsRuinedStation
```

Make variants with the same `size`, tag them with the same theme tag, and register their offsets.
The prefab generator chooses among eligible rooms and rotates square rooms; rectangular rooms may
also be turned to use a matching rotated dimension variant. Provide rooms for the dimensions your
room packs request. If no matching variant exists, that slot can be left empty (or use
`fallbackTile` on `PrefabDunGen` to fill the missing floor).

Entity placement inside a room belongs in that room map. This is the easiest way to create reliable
entity overlays: place a bed, table, loot marker, or machine at the desired coordinate in each map
variant. Use later dungeon layers for entities that should be scattered or chosen at random.

## Assemble rooms into a layout

Room pack bounds use `left,bottom,right,top` coordinates. A pack's `size` is the rectangle all its
room bounds must fit inside. A preset is a list of pack bounds placed around the origin. The stock
presets are a good template; a minimal room pack looks like:

```yaml
- type: dungeonRoomPack
  id: KsSmallRooms
  size: 17, 17
  rooms:
  - 0, 0, 7, 7
  - 9, 0, 16, 7
  - 0, 9, 7, 16
  - 9, 9, 16, 16
```

Bounds are rectangles, so check that every room fits the intended dimensions and leave enough space
for walls/corridors. Presets can be irregular: moving pack bounds around is a simple way to create
different dungeon silhouettes without changing the rooms.

Then select a room tag and preset in a config:

```yaml
- type: dungeonConfig
  id: KsRuinedStationDungeon
  layers:
  - !type:PrefabDunGen
    roomWhitelist:
      tags:
      - KsRuinedStation
    presets:
    - Bucket

  - !type:CorridorDunGen
    width: 2
    tile: FloorSteel

  - !type:BoundaryWallDunGen
    wall: WallSolid
    tile: FloorSteel
```

Layer order matters. First generate the rooms or terrain, then run layers that connect or decorate
those results. `CorridorDunGen` needs rooms with entrance edges to connect. `BoundaryWallDunGen`
places walls around generated floor boundaries. Follow nearby configs for the exact options of each
layer.

## Simple layout algorithms

These are easy algorithms to use when sketching a new generator or planning prefab variants. The
first two can be built mostly from the existing room-pack/preset approach. A new algorithm is not
automatically usable just because its pseudocode is simple: adding a native generator requires an
`IDunGenLayer` data type and server execution support in `DungeonJob`.

### 1. Room grid: rooms and corridors

Use a fixed grid of cells. Randomly choose which cells contain rooms, then connect each room to its
right and lower neighbors with a corridor. This creates readable, predictable plans.

```text
for each cell in grid:
    if randomChance(roomChance):
        place a room variant in the cell
for each placed room:
    if room to the right: connect their nearest doors
    if room below: connect their nearest doors
```

Use this when the player should understand the layout quickly: labs, bunkers, mines. In current
content, create a few presets with pack positions that express the shapes you want, then let
`PrefabDunGen` and `CorridorDunGen` do the placement and linking.

### 2. Branching rooms: a main path with side rooms

Start with an entrance and add one room at a time at an open door. Prefer extending forward, with a
smaller chance of branching left or right. Stop after a room count or depth limit.

```text
frontier = [entrance]
while roomCount < limit and frontier not empty:
    door = chooseOpenDoor(frontier)
    room = chooseRoomThatFits(door)
    attach(room, door)
    add room's other doors to frontier
```

This suits mines, derelicts, and tunnel systems. Design prefab variants with door openings on the
edges where they can attach. You can approximate the same feel today using elongated presets and
room packs with fewer branches.

### 3. Noise blobs: caves and asteroid interiors

Sample a noise function at each coordinate. If the value is above a threshold, place a floor tile;
otherwise leave it empty or solid. `NoiseDunGen` already does this kind of thresholded fill and
groups connected floor into generated areas.

```text
for each coordinate (x, y):
    if noise(x, y) > threshold:
        tiles[x, y] = caveFloor
```

Higher thresholds make smaller, sparser shapes; lower thresholds make larger connected areas.
Combine multiple noise layers with different thresholds/tiles for dirt, rock, and special floor.
`Resources/Prototypes/Procedural/vgroid.yml` shows the asteroid use of noise and a later fill pass.

### 4. Cellular caves: smooth random caves

Start with a random wall/floor grid. For each cell, count nearby walls; turn the cell into wall if
the count is high, otherwise floor. Repeat several times. This smooths jagged noise into cave-like
chambers.

```text
grid = randomWallsAndFloors()
repeat 4 times:
    next = copy(grid)
    for each cell:
        if wallNeighbors(cell) >= 5: next[cell] = wall
        else:                       next[cell] = floor
    grid = next
```

This is a useful *design* algorithm, but there is no general cellular-automata layer in the current
prototype set. For a no-code result, draw a few cave room prefabs and combine them with
`WormCorridorDunGen` or use `NoiseDunGen`. For a true cellular generator, implement a new layer.

## Entity layers and style

Style should mostly be data: keep shape generation stable and swap the palette and entity tables.

- **Floor palette**: corridor and noise generators take tile prototypes; replacement layers can
  add noise-based tile variation. Pick floor tiles that have the needed grid and collision behavior.
- **Walls and doors**: `BoundaryWallDunGen`, room entrance, junction, and window layers can finish
  spaces after floors and corridors exist. Room maps are best for exact interior walls.
- **Props and clutter**: use entity tables for random choices. Existing tables such as `BaseClutter`
  and `HauntedClutter` in `dungeon_configs.yml` are examples. Tables let one spawn point choose a
  weighted entity without duplicating generator logic.
- **Themed room sets**: make different room prototypes with different tags, then point each config's
  `roomWhitelist.tags` to its own set. The same layout algorithm can then produce a lab, cave base,
  or wreck with different floor and entity choices.
- **Terrain scatter**: `BiomeDunGen` applies a biome to matching tiles; `FillGridDunGen` places an
  entity on eligible tiles; ore and mob layers add their specific content. Use masks/allowed tiles
  to keep entities off corridors or special floors.

Example: a haunted cave can use cave floor, rock boundary walls, worm corridors, and a
`HauntedClutter` entity table. A clean lab can use steel floor, airlock entrances, wall mounts,
windows, and cable placement. Compare those configs in `dungeon_configs.yml` to see one shared
pipeline styled two ways.

## A practical build order

1. Draw one small room in a map editor and verify its floor, doors, and props.
2. Register it as `dungeonRoom`; make a second variant with the same tag and dimensions.
3. Add one room pack and use a stock preset.
4. Add a `PrefabDunGen` config, then corridors and boundary walls.
5. Add entity tables or other decorations after the basic layout works.
6. Try multiple seeds. If rooms fail to appear, check tag spelling, room dimensions, offsets,
   preset bounds, and room pack bounds first.

## When you need a new algorithm in code

The generation interface is `IDunGenLayer`, and `DungeonConfig.Layers` is an ordered list of those
layers. Server execution dispatches layer types in `Content.Server/Procedural/DungeonJob/DungeonJob.cs`.
Tile-producing examples are `DungeonJob.Noise.cs` and `DungeonJob.DunGenReplaceTile.cs`; the shared
settings for noise are in `Content.Shared/Procedural/DungeonGenerators/NoiseDunGen.cs`.

For an array-oriented generator, a straightforward design is:

```text
Tile[,] floor = generateShape(seed, width, height)
EntityLayer[,] walls = deriveBoundaryWalls(floor)
EntityLayer[,] props = scatterProps(floor, seed)
write floor tiles
spawn walls, then props
```

In actual server code, store positions in a list of tile writes and entity spawn requests, use the
provided seeded random generator so a seed reproduces the same result, respect reserved tiles, and
return/update the dungeon area so later layers know which tiles belong to it. Keep entity layers
separate from floor generation so themes can reuse the same shape algorithm. Before implementing,
read a small existing layer from its shared data class through its `DungeonJob` handler; new layer
types must be wired into that dispatch and any needed dungeon data must be recorded for later passes.

## Useful references

- `Content.Shared/Procedural/DungeonConfig.cs`: config and ordered layers.
- `Content.Shared/Procedural/DungeonRoomPrototype.cs`: room map metadata.
- `Content.Shared/Procedural/DungeonRoomPackPrototype.cs` and `DungeonPresetPrototype.cs`: layout geometry.
- `Content.Shared/Procedural/DungeonGenerators/PrefabDunGen.cs`: prefab settings.
- `Content.Server/Procedural/DungeonJob/DungeonJob.DunGenPrefab.cs`: room selection and placement.
- `Content.Server/Procedural/DungeonJob/DungeonJob.Noise.cs`: thresholded noise floors.
- `Resources/Prototypes/Procedural/dungeon_configs.yml`: themed full examples.
- `Resources/Prototypes/Procedural/Maps/bagel.yml`: room prototype examples.
