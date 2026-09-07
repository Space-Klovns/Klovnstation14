using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Player;
using Content.Server._KS14.NPC.Systems;

namespace Content.Server._KS14.NPC.Operators;

/// <summary>
/// Finds a valid target (any actor) within vision radius and writes it to the
/// specified blackboard key. Unlike NearbyGunTargets, does not require a weapon.
/// Intended for ranged bosses (colossus, etc.) that attack via pattern systems.
/// </summary>
public sealed partial class NPCRangedTargetingOperator : HTNOperator
{
    [Dependency] private NpcAggroSystem _aggro = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [DataField("key")] public string Key = "Target";
    [DataField("aggroKey")] public string AggroKey = "Aggroed";
    [DataField("radiusKey")] public string RadiusKey = "VisionRadius";       // fight range
    [DataField("radius")] public float Radius = 14f;
    [DataField("aggroRadiusKey")] public string AggroRadiusKey = "AggroVisionRadius"; // 3x3 trigger
    [DataField("aggroRadius")] public float AggroRadius = 3f;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entMan.TryGetComponent<TransformComponent>(owner, out var ownerXform))
            return HTNOperatorStatus.Failed;

        var visionRadius = blackboard.ContainsKey(RadiusKey)
            ? blackboard.GetValue<float>(RadiusKey) : Radius;
        var aggroRadius = blackboard.ContainsKey(AggroRadiusKey)
            ? blackboard.GetValue<float>(AggroRadiusKey) : AggroRadius;
        var aggroed = _aggro.IsAggroed(owner);

        var ourPos = _transform.GetWorldPosition(ownerXform);

        EntityUid? bestTarget = null;
        var bestDist = visionRadius * visionRadius;
        var aggroDistSq = aggroRadius * aggroRadius;

        var query = _entMan.EntityQuery<TransformComponent, ActorComponent>(true);
        foreach (var (xform, _) in query)
        {
            if (xform.MapID != ownerXform.MapID)
                continue;

            var targetUid = xform.Owner;
            if (targetUid == owner)
                continue;

            if (_entMan.TryGetComponent<MobStateComponent>(targetUid, out var mob) &&
                mob.CurrentState != MobState.Alive)
                continue;

            var targetPos = _transform.GetWorldPosition(xform);
            var distSq = Vector2.DistanceSquared(ourPos, targetPos);

            // Not aggroed yet? Only actors within 3 tiles can wake it.
            if (!aggroed && distSq > aggroDistSq)
                continue;

            if (distSq < bestDist)
            {
                bestDist = distSq;
                bestTarget = targetUid;
            }
        }

        if (bestTarget == null)
            return HTNOperatorStatus.Failed;

        blackboard.SetValue(Key, bestTarget.Value);

        // Proximity-triggered aggro: persist so the system's IsAggroed agrees.
        if (!aggroed)
            _aggro.Aggro(owner, bestTarget.Value);

        return HTNOperatorStatus.Finished;
    }

}
