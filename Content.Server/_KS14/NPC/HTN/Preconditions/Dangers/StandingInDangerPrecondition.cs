using Content.Server._KS14.NPC.Dangers;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Server.NPC.Systems;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Dangers;

/// <summary>
///     Met while the owner is stopped on a tile posing what it avoids (see <see cref="NPCSteeringSystem.IsStandingInDanger"/>),
///         of <see cref="Dangers"/>.
/// </summary>
public sealed partial class StandingInDangerPrecondition : HTNPrecondition
{
    [Dependency] private NPCSteeringSystem _npcSteeringSystem = default!;

    /// <summary>
    ///     Which of what the owner avoids count here. A danger that another branch gets it away from - a grenade's blast, by
    ///         the grenade branch, which dives and calls it out - is left out, or this one would take it over.
    /// </summary>
    [DataField]
    public NpcTileDangerKind Dangers = NpcTileDangerKind.Fire | NpcTileDangerKind.Blast;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return _npcSteeringSystem.IsStandingInDanger(blackboard.GetOwner(), Dangers);
    }
}
