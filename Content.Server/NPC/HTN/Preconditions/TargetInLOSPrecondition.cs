using Content.Server._KS14.NPC.LineOfFire; // KS14
using Content.Server._KS14.NPC.Squad; // KS14
using Content.Server.Interaction;
using Content.Shared.NPC.Systems; // KS14
using Content.Shared.Physics;

namespace Content.Server.NPC.HTN.Preconditions;

public sealed partial class TargetInLOSPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    private InteractionSystem _interaction = default!;
    private NpcFactionSystem _npcFactionSystem = default!; // KS14: friends in the way, see NpcFireLaneMode.FireThrough
    private NpcLineOfFireSystem _npcLineOfFireSystem = default!; // KS14: bodies on the floor, see IsLyingMob
    private NpcSquadFireLaneSystem _npcSquadFireLaneSystem = default!; // KS14: friends in the way, see NpcFireLaneMode.FireThrough

    [DataField("targetKey")]
    public string TargetKey = "Target";

    [DataField("rangeKey")]
    public string RangeKey = "RangeKey";

    [DataField("opaqueKey")]
    public bool UseOpaqueForLOSChecksKey = true;

    // KS14 start
    /// <summary>
    ///     The blackboard key that, set, wins over <see cref="KsStandingMobsBlock"/>: a switch per NPC. Read by
    ///         <c>GunOperator</c> too.
    /// </summary>
    public const string KsStandingMobsBlockSightKey = "KsStandingMobsBlockSight";

    /// <summary>
    ///     Whether a mob standing between hides the target. One lying down never does. A turret that tells sight from shot
    ///         (<c>GunOperator.KsHoldFireWhenBlocked</c>) turns it off, and holds fire for a friend in the way instead.
    /// </summary>
    [DataField("ksStandingMobsBlock")]
    public bool KsStandingMobsBlock = true;
    // KS14 end

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _interaction = sysManager.GetEntitySystem<InteractionSystem>();
        _npcFactionSystem = sysManager.GetEntitySystem<NpcFactionSystem>(); // KS14
        _npcLineOfFireSystem = sysManager.GetEntitySystem<NpcLineOfFireSystem>(); // KS14
        _npcSquadFireLaneSystem = sysManager.GetEntitySystem<NpcSquadFireLaneSystem>(); // KS14
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return false;

        var range = blackboard.GetValueOrDefault<float>(RangeKey, _entManager);
        var collisionGroup = UseOpaqueForLOSChecksKey ? CollisionGroup.Opaque : (CollisionGroup.Impassable | CollisionGroup.InteractImpassable);

        // KS14 start: every mob is on the Opaque layer, so a body on the floor between them would hide the target: it hides
        //      nothing, and rounds pass over it. Nor, if asked, does one standing
        var standingMobsBlock = blackboard.TryGetValue<bool>(KsStandingMobsBlockSightKey, out var standingMobsBlockValue, _entManager)
            ? standingMobsBlockValue
            : KsStandingMobsBlock;

        if (!standingMobsBlock)
            return _interaction.InRangeUnobstructed(owner, target, range, collisionGroup, predicate: _npcLineOfFireSystem.IsMob);

        var isLyingMob = _npcLineOfFireSystem.IsLyingMob;

        // One that fires through friends does not lose sight of its target behind one
        if (_npcSquadFireLaneSystem.GetFireLaneMode(owner) == NpcFireLaneMode.FireThrough)
        {
            return _interaction.InRangeUnobstructed(owner, target, range: range, collisionMask: collisionGroup,
                predicate: other => isLyingMob(other) ||
                    (_entManager.HasComponent<Content.Shared.Mobs.Components.MobStateComponent>(other) &&
                    _npcFactionSystem.IsEntityFriendly(owner, other)));
        }

        return _interaction.InRangeUnobstructed(owner, target, range, collisionGroup, predicate: isLyingMob);
        // KS14 end
    }
}
