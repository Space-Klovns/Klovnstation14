using Content.Server.NPC;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN;

/// <summary>
///     The blackboard reads the NPC code makes over and over. They add nothing to what
///         <see cref="NPCBlackboard"/> does itself; they only say it shorter.
/// </summary>
public static class NpcBlackboardExtensions
{
    /// <summary>
    ///     The entity the blackboard belongs to. Throws if the blackboard has none, like
    ///         <see cref="NPCBlackboard.GetValue{T}"/>.
    /// </summary>
    public static EntityUid GetOwner(this NPCBlackboard blackboard)
    {
        return blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
    }

    /// <summary>
    ///     The entity the blackboard belongs to, if it has one.
    /// </summary>
    public static bool TryGetOwner(this NPCBlackboard blackboard, IEntityManager entityManager, out EntityUid ownerUid)
    {
        return blackboard.TryGetValue(NPCBlackboard.Owner, out ownerUid, entityManager);
    }

    /// <summary>
    ///     The entity at <paramref name="key"/>, if the blackboard has one there that is not deleted or being deleted
    ///         (<see cref="NPCBlackboard.TryGetValue{T}"/> treats one that is as missing).
    /// </summary>
    public static bool TryGetEntity(this NPCBlackboard blackboard, string key, IEntityManager entityManager, out EntityUid entityUid)
    {
        return blackboard.TryGetValue(key, out entityUid, entityManager);
    }

    /// <summary>
    ///     The coordinates at <paramref name="key"/>, if the blackboard has some there.
    /// </summary>
    public static bool TryGetCoordinates(this NPCBlackboard blackboard, string key, IEntityManager entityManager, out EntityCoordinates coordinates)
    {
        return blackboard.TryGetValue(key, out coordinates, entityManager);
    }

    /// <summary>
    ///     The float at <paramref name="key"/> if there is a key and the blackboard has it, or <paramref name="fallback"/>.
    /// </summary>
    public static float GetFloatOr(this NPCBlackboard blackboard, string? key, float fallback, IEntityManager entityManager)
    {
        return key is not null && blackboard.TryGetValue<float>(key, out var value, entityManager) ? value : fallback;
    }
}
