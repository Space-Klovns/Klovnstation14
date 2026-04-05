using Content.Shared.Body;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Klovnmed.RemoveRandomOrgan;

/// <summary>
///     Removes a random organ on mapinit, then deletes the component.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RemoveRandomOrganComponent : Component
{
    /// <summary>
    ///     Categories eligible.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadOnly)]
    public HashSet<ProtoId<OrganCategoryPrototype>> Categories = [];
}
