using Content.Server._KS14.NPC.LineOfFire;
using Content.Server.Interaction;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._KS14.WeldedParts;

/// <summary>
///     Whether what does the shooting has the target in sight and in range: the owner's welded parts that carry a gun (a
///         tank's turret), or the owner itself if it has one. Sight as a turret has it (<c>EnergyTurretCompound</c>): the
///         Opaque layer, mobs and the whole it belongs to not counting. A hull can see what its turret cannot (it looks for
///         walls, not for anything Opaque), and one stopping there waits on a turret that never fires. With no gun at all,
///         the owner's own sight.
/// </summary>
public sealed partial class KsWeldedGunSeesTargetPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private InteractionSystem _interactionSystem = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;
    [Dependency] private KsWeldedPartsSystem _weldedPartsSystem = default!;
    [Dependency] private EntityQuery<GunComponent> _gunQuery = default!;

    [DataField]
    public string TargetKey = "Target";

    [DataField(required: true)]
    public string RangeKey = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var ownerUid, _entityManager) ||
            !blackboard.TryGetValue<EntityUid>(TargetKey, out var targetUid, _entityManager))
            return false;

        var range = blackboard.GetValueOrDefault<float>(RangeKey, _entityManager);

        var anyGun = _gunQuery.HasComp(ownerUid);
        if (anyGun && Sees(ownerUid, ownerUid, targetUid, range))
            return true;

        foreach (var partUid in _weldedPartsSystem.GetParts(ownerUid))
        {
            if (!_gunQuery.HasComp(partUid))
                continue;

            anyGun = true;
            if (Sees(ownerUid, partUid, targetUid, range))
                return true;
        }

        // Nothing to shoot with: its own eyes, then.
        return !anyGun && Sees(ownerUid, ownerUid, targetUid, range);
    }

    private bool Sees(EntityUid rootUid, EntityUid shooterUid, EntityUid targetUid, float range)
    {
        var isMob = _npcLineOfFireSystem.IsMob;
        return _interactionSystem.InRangeUnobstructed(shooterUid,
            targetUid,
            range,
            CollisionGroup.Opaque,
            predicate: uid => isMob(uid) || _weldedPartsSystem.IsOfWhole(rootUid, uid));
    }
}
