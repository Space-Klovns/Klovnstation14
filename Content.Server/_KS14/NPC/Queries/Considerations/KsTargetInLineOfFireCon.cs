using Content.Server._KS14.NPC.LineOfFire;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._KS14.NPC.Queries.Considerations;

/// <summary>
///     1 if the owner's next round would get to the target by what the round collides with, 0 if something is in the way:
///         glass stops a bullet and not a laser, and mobs do not count (<see cref="NpcLineOfFireSystem.CanRoundReach"/>).
///         Not <see cref="TargetInLOSCon"/>: a turret sees through a window it cannot shoot through. The same check that has
///         a turret hold fire, so a target this scores 0 is one it would only aim at.
/// </summary>
/// <remarks>
///     One ray per candidate, without allocating. Give it a <c>BoolCurve</c> with a small <c>lowScore</c> rather than 0, so
///         that a target its rounds cannot reach is still picked, and aimed at, when it is the only one; put it last in the
///         query, so it only casts for candidates that could still win.
/// </remarks>
public sealed partial class KsTargetInLineOfFireCon : UtilityConsideration
{
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        return _npcLineOfFireSystem.CanRoundReach(ownerUid, targetUid) ? 1f : 0f;
    }
}
