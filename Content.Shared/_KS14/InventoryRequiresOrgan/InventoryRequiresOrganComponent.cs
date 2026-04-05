using Content.Shared.Body;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.InventoryRequiresOrgan;

[RegisterComponent, NetworkedComponent]
public sealed partial class InventoryRequiresOrganComponent : Component
{
    /// <summary>
    ///     Dictionary of inventory slot IDs by the organ categories
    ///         that must be present for them to be usable.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<string, List<ProtoId<OrganCategoryPrototype>>> Categories = [];
}
