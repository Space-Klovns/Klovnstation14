using Content.Server._KS14.NPC.LineOfFire;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Robust.Shared.Physics;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met while the owner could hit what is at <see cref="TargetKey"/> from where it stands, whether or not it can see it.
///         See <see cref="NpcLineOfFireSystem"/>.
/// </summary>
public sealed partial class LineOfFirePrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;

    private const float Range = 64f;

    [DataField(required: true)]
    public string TargetKey = string.Empty;

    [DataField]
    public bool Invert;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var targetUid, _entityManager))
            return Invert;

        return _npcLineOfFireSystem.HasLineOfFire(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), targetUid, Range) != Invert;
    }
}
