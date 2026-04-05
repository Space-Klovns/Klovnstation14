using Content.Shared.Body;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.WormWithoutOrgans;

/// <summary>
///     Gives the entity <see cref="WormComponent"/> if it
///         doesnt have all of the specified organs.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WormWithoutOrgansComponent : Component
{
    /// <summary>
    ///     Categories required.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadOnly)]
    public List<ProtoId<OrganCategoryPrototype>> Categories = [];
}
