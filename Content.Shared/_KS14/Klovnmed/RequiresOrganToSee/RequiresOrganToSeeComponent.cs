using Content.Shared.Body;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Klovnmed.RequiresOrganToSee;

[RegisterComponent, NetworkedComponent]
public sealed partial class RequiresOrganToSeeComponent : Component
{
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadOnly)]
    public ProtoId<OrganCategoryPrototype> Category;
}
