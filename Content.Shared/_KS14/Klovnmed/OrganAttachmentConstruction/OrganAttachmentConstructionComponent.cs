using Content.Shared.Body;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Klovnmed.OrganAttachmentConstruction;

/// <summary>
///     When on something with <see cref="OrganAttachmentOperation.OrganAttachmentOperationComponent"/>,
///         will only allow attaching organs if entitys ConstructionComponent
///         is on a specific node.
///
///     Base organ categories (of the operation component) are always allowed.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(fieldDeltas: true)]
[Access(typeof(SharedOrganAttachmentConstructionSystem))]
public sealed partial class OrganAttachmentConstructionComponent : Component
{
    /// <summary>
    ///     Key being a construction node name, and value
    ///         being what organ categories it accepts (a whitelist), or null if all are accepted.
    /// </summary>
    [DataField("nodes")]
    [ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<string, HashSet<ProtoId<OrganCategoryPrototype>>?> NodeMap = [];

    /// <summary>
    ///     Organs that are always attachable no matter what.
    ///         Key being the organ, and value being how many things
    ///         are contributing to it being attachable.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<ProtoId<OrganCategoryPrototype>, byte> AlwaysAttachable = [];

    /// <summary>
    ///    Current node name, networked.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public string? NetNode = null;

    /// <summary>
    ///     Whether the categories in <see cref="OrganAttachmentOperation.OrganAttachmentOperationComponent.BaseOrganCategories"/>
    ///         are always attachable.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool BaseOrgansAlwaysAttachable = false;

    /// <summary>
    ///     Organs are always attachable to this entity, if its
    ///         in the body.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool OrgansAlwaysAttachableWhenInBody = true;
}
