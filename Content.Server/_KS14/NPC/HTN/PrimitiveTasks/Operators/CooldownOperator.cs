using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class CooldownOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NpcGenericCooldownSystem _genericCooldownSystem = default!;

    /// <summary>
    ///     ID of the cooldown.
    /// </summary>
    [DataField(required: true)] public string Id;

    /// <summary>
    ///     Duration of the cooldown.
    /// </summary>
    [DataField(required: true)] public TimeSpan Duration;

    private int _idHash;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _idHash = Id.GetHashCode();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var ownerUid, _entityManager))
            return HTNOperatorStatus.Failed;

        _genericCooldownSystem.SetCooldown(ownerUid, _idHash, _gameTiming.CurTime + Duration);
        return HTNOperatorStatus.Finished;
    }
}
