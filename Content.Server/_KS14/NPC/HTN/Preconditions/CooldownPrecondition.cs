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

    /// <summary>
    ///     ID of the cooldown.
    /// </summary>
    [DataField] public string Id;
    private int _idHash;

    [DataField] public bool Inverted;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _idHash = Id.GetHashCode();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var ownerUid, _entityManager))
            return Inverted;

        return _genericCooldownSystem.IsKeyOnCooldown(ownerUid, _idHash) ^ Inverted;
    }
}
