using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Returns true (or false if inverted) only when the entity at the key
///         exists and has all of the specified components.
/// </summary>
public sealed partial class KeyComponentPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField] public bool Invert;

    [DataField] public string Key = "Target";
    [DataField(required: true)] public ComponentRegistry Components = new();

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(Key, out var uid, _entManager))
            return Invert;

        foreach (var comp in Components)
        {
            if (!_entManager.HasComponent(uid, comp.Value.Component.GetType()))
                return Invert;
        }

        return !Invert;
    }
}
