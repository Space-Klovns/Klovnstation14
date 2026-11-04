using Content.Server._KS14.NPC.Squad;
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
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    /// <summary>
    ///     ID of the cooldown.
    /// </summary>
    [DataField(required: true)] public string Id;

    /// <summary>
    ///     Duration of the cooldown.
    /// </summary>
    [DataField(required: true)] public TimeSpan Duration;

    /// <summary>
    ///     Whether this is shared by the owner's whole squad rather than its own, so that one member setting it holds them
    ///         all back. On its own, an NPC keeps it to itself.
    /// </summary>
    [DataField] public bool SquadWide;

    /// <summary>
    ///     Fails if the cooldown is already running, rather than starting it again. For a cooldown that holds something
    ///         back, placed before the thing itself: <c>CooldownPrecondition</c> alone cannot, as NPCs planning side by
    ///         side all pass it before any of them gets here. Only the first to get here carries on.
    /// </summary>
    [DataField] public bool FailIfRunning;

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

        var holderUid = SquadWide ? _npcSquadSystem.GetSquadOrSelf(ownerUid) : ownerUid;

        if (FailIfRunning && _genericCooldownSystem.IsKeyOnCooldown(holderUid, _idHash))
            return HTNOperatorStatus.Failed;

        _genericCooldownSystem.SetCooldown(holderUid, _idHash, _gameTiming.CurTime + Duration);
        return HTNOperatorStatus.Finished;
    }
}
