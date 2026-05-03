using Content.Shared.Body;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Klovnmed.OrganAttachmentOperation;

/// <summary>
///     Allows organs to be attached to this entity, if its a body or another organ.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OrganAttachmentOperationComponent : Component
{
    /// <summary>
    ///     Organ categories that can be attached.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public HashSet<ProtoId<OrganCategoryPrototype>> BaseOrganCategories = [];

    /// <summary>
    ///     Dictionary that describes what organs (key) go into which other organs (value).
    ///         If there is no value for a specific organ being inserted, it will just be inserted
    ///         at the root of the hierarchy: the body.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<ProtoId<OrganCategoryPrototype>, ProtoId<OrganCategoryPrototype>> OrganRoutes = [];
}

/// <summary>
///     Raised on something with <see cref="OrganAttachmentOperationComponent"/> to
///         attempt to cancel an organ attachment if necessary, with the category of the organ being attached.
///
///     This will be raised every tick during doafters.
/// </summary>
[ByRefEvent]
public record struct CanAttachOrganEvent(bool Cancelled, ProtoId<OrganCategoryPrototype> Category, OrganAttachmentOperationComponent? Component);

/// <summary>
///     Raised on something with <see cref="OrganAttachmentOperationComponent"/> to
///         get the organ categories which may be inserted, and into where.
/// </summary>
/// <param name="Categories"></param>
[ByRefEvent]
public record struct OrganAttachmentGetCategoriesEvent(HashSet<ProtoId<OrganCategoryPrototype>>? Categories = null)
{
    public void Add(ProtoId<OrganCategoryPrototype> category)
    {
        (Categories ??= []).Add(category);
    }
}

// Why cant i use parameter list on the type instead of dedicated constructor
// I will never know
[Serializable, NetSerializable]
public sealed partial class OrganAttachmentDoAfterEvent : DoAfterEvent
{
    public ProtoId<OrganCategoryPrototype> Category;

    /// <summary>
    ///     Entity that will contain this inserted organ.
    /// </summary>
    public NetEntity ContainerEntity;

    public OrganAttachmentDoAfterEvent(ProtoId<OrganCategoryPrototype> category, NetEntity containerEntity)
    {
        Category = category;
        ContainerEntity = containerEntity;
    }

    public override DoAfterEvent Clone() => this;
}
