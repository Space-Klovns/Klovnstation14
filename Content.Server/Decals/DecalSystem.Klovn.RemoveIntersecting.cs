using Content.Shared.Decals;
using Robust.Shared.Collections;
using Robust.Shared.GameStates;

namespace Content.Server.Decals;

public sealed partial class DecalSystem : SharedDecalSystem
{
    /// <summary>
    ///     This is preferred over GetDecalsIntersecting and then
    ///         spamming RemoveDecals, as this gets straight to the point.
    /// </summary>
    public void KsRemoveDecalsIntersecting(EntityUid gridUid, Box2 bounds)
    {
        // DirtyChunk deletes the chunk entity once its last decal goes, which would invalidate the
        // enumerator, so take a snapshot of the intersecting chunks before touching any of them.
        var chunks = new ValueList<Entity<ChunkEntityComponent, DecalChunkComponent>>();

        foreach (var chunk in ChunkEntities.GetChunksIntersecting(gridUid, bounds, DecalChunkQuery))
        {
            chunks.Add(chunk);
        }

        var toRemove = new ValueList<ushort>();

        foreach (var chunk in chunks)
        {
            toRemove.Clear();

            foreach (var (decalId, decal) in chunk.Comp2.Decals)
            {
                if (!bounds.Contains(decal.Coordinates))
                    continue;

                toRemove.Add(decalId);
            }

            if (toRemove.Count == 0)
                continue;

            foreach (var decalId in toRemove)
            {
                chunk.Comp2.Decals.Remove(decalId);
                FreeDecalId(chunk.Comp2, decalId);
            }

            DirtyChunk(chunk);
        }
    }
}
