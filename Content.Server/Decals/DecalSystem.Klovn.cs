using System.Numerics;
using Content.Shared.Decals;
using ChunkIndicesEnumerator = Robust.Shared.Map.Enumerators.ChunkIndicesEnumerator;

namespace Content.Server.Decals;

public sealed partial class DecalSystem : SharedDecalSystem
{
    private static readonly Vector2 KsDecalChunkSize = new Vector2(ChunkSize, ChunkSize) / 2f;

    /// <summary>
    ///     This is preferred over GetDecalsIntersecting and then
    ///         spamming RemoveDecals, as this gets straight to the point.
    /// </summary>

    // OnDecalRemoved isnt called anywhere here lol
    public void KsRemoveDecalsIntersecting(Entity<DecalGridComponent?> entity, Box2 bounds)
    {
        if (!Resolve(entity.Owner, ref entity.Comp))
            return;

        var chunkCollection = entity.Comp.ChunkCollection.ChunkCollection;
        var chunks = new ChunkIndicesEnumerator(bounds, ChunkSize);

        while (chunks.MoveNext(out var chunkOrigin))
        {
            if (chunkOrigin is not { } ||
                !chunkCollection.TryGetValue(chunkOrigin.Value, out var chunk))
                continue;

            // If the chunk is fully contained in the area we want to remove, just nuke it
            var chunkBox = Box2.CenteredAround((Vector2)chunkOrigin.Value, KsDecalChunkSize);
            if (bounds.Contains(chunkBox))
            {
                chunkCollection.Remove(chunkOrigin.Value);
                DirtyChunk(entity.Owner, chunkOrigin.Value, chunk);

                continue;
            }

            foreach (var (id, decal) in chunk.Decals)
            {
                if (!bounds.Contains(decal.Coordinates))
                    continue;

                chunk.Decals.Remove(id);
            }

            DirtyChunk(entity.Owner, chunkOrigin.Value, chunk);
        }
    }
}
