# Sprite-generation guidelines

## General

## Sprite atlases

Your method of sprite generation will most likely be the downscaling of an externally generated sprite. The sprite generator is fickle, and maintaining consistency is very important - this is why we use sprite atlases. When making a family of sprites, have them generate in a massive monolith that you then cut apart. This will have it create all of them in one go, likely with a fitting art style.

## Tiles

### Tile size terminology.

Tiles are 32x32 pixels in size.

A tile begins with an archetype. For example, we have a steel tile. It can have families, like light steel tile or dark steel tile. The steel tile itself also constitutes a family. Each family has versions - light steel tile clean, light steel tile damaged, light steel tile grimy. Finally, each version has variants - slightly visually modified sprites of the same tile.

### Tile variantisation

The same tile needs to be made in slightly different variants for the tile variantisation system. The amount of these variants is set to 4 per tile type uniformly in our codebase, as this suffices. This is key: when generating a tile version, the variants must be slight. You do not wish to introduce "variants" that look completely different. This means that you will generate 4 clean, 4 dirty, etc. tiles. Each tile version will as such be a 128x32 pixel spritesheet.

Archetypes group tiles into ones with a common unpainted geometry - a steel tile will have the same underlying unpainted geometry as a dark steel tile. Families group tiles by precise common sprite by specifying the color - a light steel tile will have the same underlying tile in all of its damaged, dirty or clean versions. Versions group tiles per base peculiarisation, and variants add innate variety to versions.

### Tile sprite atlases

Tile sprites should be grouped into atlases for consistency. The clean, damaged, dirty and very dirty versions of a tile should look alike, since they share the same family. Group tiles into atlases by archetypes. For example, a sample techmaint tile has one archetype (techmaint), one family (techmaint, there are no light or dark techmaints), 4 versions (clean, damaged, dirty, very dirty) and 4 variants per version. It will as such produce a 4x4 atlas - only variants go right, versions go down. A sample steel tile has 1 archetype (steel), three families (light, base, dark), 4 versions per family (clean, damaged, dirty, very dirty) and 4 variants per version. It will as such produce a 4x12 atlas - 12 versions in total, 4 variants per version.

Downscale the complete atlas once with a crisp pixel-preserving filter, then slice it into the exact engine sprite sheets. Verify every output sheet's dimensions, opacity, and tile-edge continuity.

### Designing tiles

The visibly "stepped-on" area of a tile should form most of its surface. Every tile in a family should be expected to be used and reasonably well fit together with any other tile in its family. Since you are scaling down a much larger sprite, ensure that the geometry holds even as the sprite is downscaled - soft gradient edges scale down very badly. Avoid blur. Prefer simple, large geometric forms with straight, high-contrast edges: plates, seams, bevels, bolt heads, and support gaps. Avoid fine noise, soft gradients, painterly texture, and tiny incidental detail; they blur or disappear during downscaling.
