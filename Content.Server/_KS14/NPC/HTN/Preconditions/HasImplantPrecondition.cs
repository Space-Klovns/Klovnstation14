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
public sealed partial class HasImplantPrecondition : KsInvertiblePrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private ContainerSystem _containerSystem = default!;

    /// <summary>
    ///     Entity prototype ID of the implant being checked for.
    /// </summary>
    [DataField(required: true)] public EntProtoId Id = "";

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetOwner(_entManager, out var ownerUid) ||
            !_containerSystem.TryGetContainer(ownerUid, ImplanterComponent.ImplantSlotId, out var implantContainer) ||
            implantContainer.Count == 0)
            return false;

        foreach (var containedImplantUid in implantContainer.ContainedEntities)
        {
            if (_entManager.GetComponent<MetaDataComponent>/* Can't EntityQuery a MetaDataComp here apparently */(containedImplantUid).EntityPrototype?.ID != Id.ToString())
                continue;

            return true;
        }

        return false;
    }
}
