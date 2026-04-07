using Content.Shared.Body;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Execution;

/// <summary>
///     Specifies organs that should be removed when doing a gun execution.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GunExecutionOrganRemovalComponent : Component
{
    [DataField(required: true)]
    public ProtoId<OrganCategoryPrototype> RemovedCategory;
}
