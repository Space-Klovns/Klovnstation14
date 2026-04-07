using Content.Shared._KS14.Hierarchy; // KS14
using Content.Shared._KS14.Klovnmed; // KS14
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Body;

/// <summary>
/// Marks an entity as being able to be inserted into an entity with <seealso cref="BodyComponent" />.
/// </summary>
/// <seealso cref="BodySystem" />
[RegisterComponent, NetworkedComponent/*, AutoGenerateComponentState KS14: Removed this*/]
[Access(typeof(BodySystem), typeof(BodyHierarchySystem) /* KS14: Klovnmed access */)]
public sealed partial class OrganComponent : Component, IHierarchyElementComponent // KS14: IHierarchyElementComponent
{
    // KS14
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? HierarchyUid { get; set; }

    // KS14
    [ViewVariables(VVAccess.ReadOnly)]
    public HashSet<EntityUid> ChildUids { get; set; }

    // KS14
    [ViewVariables(VVAccess.ReadOnly)]
    public Robust.Shared.Containers.Container Container { get; set; }

    /// <summary>
    /// The body entity containing this organ, if any
    /// </summary>
    //[DataField, AutoNetworkedField] // KS14: Removed datafield
    public EntityUid? Body => HierarchyUid; // KS14: Made this point to hierarchy

    /// <summary>
    /// What kind of organ is this, if any
    /// </summary>
    [DataField]
    public ProtoId<OrganCategoryPrototype>? Category;
}
