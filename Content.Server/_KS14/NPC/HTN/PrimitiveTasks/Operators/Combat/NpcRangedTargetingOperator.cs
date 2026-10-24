using System.Numerics;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Combat;

/// <summary>
///     Picks the nearest living player within <see cref="VisionRadius"/> as the target, for NPCs that attack through
///         patterns rather than a held weapon. Until the NPC is aggroed (<see cref="NpcAggroSystem"/>), only a player
///         within <see cref="AggroRadius"/> counts, and picking one aggroes it.
/// </summary>
public sealed partial class NpcRangedTargetingOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcAggroSystem _npcAggroSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    ///     Blackboard key the target is written to.
    /// </summary>
    [DataField]
    public string TargetKey = "Target";

    /// <summary>
    ///     Blackboard key of how far away, in tiles, a target may be once aggroed. <see cref="VisionRadius"/> if unset.
    /// </summary>
    [DataField]
    public string VisionRadiusKey = "VisionRadius";

    [DataField]
    public float VisionRadius = 14f;

    /// <summary>
    ///     Blackboard key of how close, in tiles, a player must come to aggro the NPC. <see cref="AggroRadius"/> if
    ///         unset.
    /// </summary>
    [DataField]
    public string AggroRadiusKey = "AggroVisionRadius";

    [DataField]
    public float AggroRadius = 3f;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var ownerMapCoordinates = _transformSystem.GetMapCoordinates(owner);

        var aggroed = _npcAggroSystem.IsAggroed(owner);
        var radius = aggroed
            ? GetRadius(blackboard, VisionRadiusKey, VisionRadius)
            : GetRadius(blackboard, AggroRadiusKey, AggroRadius);

        EntityUid? bestTargetUid = null;
        var bestDistanceSquared = radius * radius;

        var actorEnumerator = _entityManager.EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actorEnumerator.MoveNext(out var actorUid, out _, out var actorTransformComponent))
        {
            if (actorUid == owner ||
                actorTransformComponent.MapID != ownerMapCoordinates.MapId ||
                !_mobStateSystem.IsAlive(actorUid))
                continue;

            var actorPosition = _transformSystem.GetWorldPosition(actorTransformComponent);
            var distanceSquared = Vector2.DistanceSquared(ownerMapCoordinates.Position, actorPosition);
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            bestTargetUid = actorUid;
        }

        if (bestTargetUid is not { } targetUid)
            return HTNOperatorStatus.Failed;

        blackboard.SetValue(TargetKey, targetUid);

        if (!aggroed)
            _npcAggroSystem.Aggro(owner, targetUid);

        return HTNOperatorStatus.Finished;
    }

    private float GetRadius(NPCBlackboard blackboard, string key, float fallback)
    {
        return blackboard.TryGetValue<float>(key, out var radius, _entityManager) ? radius : fallback;
    }
}
