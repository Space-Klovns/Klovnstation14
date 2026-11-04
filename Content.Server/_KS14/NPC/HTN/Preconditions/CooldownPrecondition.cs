using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Returns true (or false if inverted) if the specified cooldown is active.
/// </summary>
public sealed partial class CooldownPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcGenericCooldownSystem _genericCooldownSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;

    /// <summary>
    ///     ID of the cooldown.
    /// </summary>
    [DataField] public string Id;
    private int _idHash;

    [DataField] public bool Inverted;

    /// <summary>
    ///     Whether this is a cooldown shared by the owner's whole squad. See <see cref="CooldownOperator.SquadWide"/>.
    /// </summary>
    [DataField] public bool SquadWide;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _idHash = Id.GetHashCode();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var ownerUid, _entityManager))
            return Inverted;

        var holderUid = SquadWide ? _npcSquadSystem.GetSquadOrSelf(ownerUid) : ownerUid;
        return _genericCooldownSystem.IsKeyOnCooldown(holderUid, _idHash) ^ Inverted;
    }
}
