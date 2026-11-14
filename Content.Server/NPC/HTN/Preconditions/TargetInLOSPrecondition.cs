using Content.Server.Interaction;
using Content.Shared.NPC.Systems; // KS14
using Content.Shared.Physics;

namespace Content.Server.NPC.HTN.Preconditions;

public sealed partial class TargetInLOSPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    private InteractionSystem _interaction = default!;
    private NpcFactionSystem _npcFactionSystem = default!; // KS14: friends in the way, see IgnoreFriendlyBlocking

    [DataField("targetKey")]
    public string TargetKey = "Target";

    [DataField("rangeKey")]
    public string RangeKey = "RangeKey";

    [DataField("opaqueKey")]
    public bool UseOpaqueForLOSChecksKey = true;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _interaction = sysManager.GetEntitySystem<InteractionSystem>();
        _npcFactionSystem = sysManager.GetEntitySystem<NpcFactionSystem>(); // KS14
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return false;

        var range = blackboard.GetValueOrDefault<float>(RangeKey, _entManager);
        var collisionGroup = UseOpaqueForLOSChecksKey ? CollisionGroup.Opaque : (CollisionGroup.Impassable | CollisionGroup.InteractImpassable);

        // KS14 start: a turret that fires through friends does not lose sight of its target behind one
        if (_entManager.TryGetComponent(owner, out Content.Server._KS14.NPC.Components.KsNpcAimTrackingComponent? aimTrackingComponent) &&
            aimTrackingComponent.IgnoreFriendlyBlocking)
        {
            return _interaction.InRangeUnobstructed(owner, target, range: range, collisionMask: collisionGroup,
                predicate: friend => _entManager.HasComponent<Content.Shared.Mobs.Components.MobStateComponent>(friend) &&
                    _npcFactionSystem.IsEntityFriendly(owner, friend));
        }
        // KS14 end

        return _interaction.InRangeUnobstructed(owner, target, range, collisionGroup);
    }
}
