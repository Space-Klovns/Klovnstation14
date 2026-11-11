using Content.Server._KS14.NPC.Hiding;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Robust.Shared.Random;

namespace Content.Server._KS14.NPC.HTN.Preconditions.Hiding;

/// <summary>
///     Rolls the owner's <see cref="NpcClosetHealerComponent.Chance"/>: never met without one. A fresh roll each plan, so on its
///         own it would come up within a second or two. The closet branch starts a cooldown, as the plain heal after it does,
///         and that is what makes the roll stand for the heal.
/// </summary>
public sealed partial class ClosetHealChancePrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return _entityManager.TryGetComponent<NpcClosetHealerComponent>(blackboard.GetOwner(), out var closetHealerComponent) &&
            _robustRandom.Prob(Math.Clamp(closetHealerComponent.Chance, 0f, 1f));
    }
}
