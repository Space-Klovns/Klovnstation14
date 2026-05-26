using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Implants.Components;
using Robust.Server.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Returns true (or false if inverted) only when the entity at the key
///         exists and has all of the specified components.
/// </summary>
public sealed partial class HasImplantPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly ContainerSystem _containerSystem = default!;

    [DataField] public bool Invert;
    /// <summary>
    ///     Entity prototype ID of the implant being checked for.
    /// </summary>
    [DataField(required: true)] public EntProtoId Id = "";

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var ownerUid, _entManager) ||
            !_containerSystem.TryGetContainer(ownerUid, ImplanterComponent.ImplantSlotId, out var implantContainer) ||
            implantContainer.Count == 0)
            return Invert;

        foreach (var containedImplantUid in implantContainer.ContainedEntities)
        {
            if (_entManager.GetComponent<MetaDataComponent>/* Can't EntityQuery a MetaDataComp here apparently */(containedImplantUid).EntityPrototype?.ID != Id.ToString())
                continue;

            return !Invert;
        }

        return Invert;
    }
}
