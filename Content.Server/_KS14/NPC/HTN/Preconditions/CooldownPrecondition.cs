using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met (or not met, with <c>invert</c>) while the specified cooldown is active.
/// </summary>
public sealed partial class CooldownPrecondition : KsInvertiblePrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcGenericCooldownSystem _genericCooldownSystem = default!;

    /// <summary>
    ///     ID of the cooldown.
    /// </summary>
    [DataField] public string Id;
    private int _idHash;

    /// <summary>
    ///     Whether this is a cooldown shared by the owner's whole squad. See <see cref="CooldownOperator.SquadWide"/>.
    /// </summary>
    [DataField] public bool SquadWide;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _idHash = Id.GetHashCode();
    }

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetOwner(_entityManager, out var ownerUid))
            return false;

        var holderUid = _genericCooldownSystem.GetHolder(ownerUid, SquadWide);
        return _genericCooldownSystem.IsKeyOnCooldown(holderUid, _idHash);
    }
}
