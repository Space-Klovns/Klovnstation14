using Content.Shared.Body;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Klovnmed.OrganAttachmentOperation;

[RegisterComponent, NetworkedComponent]
[Access(typeof(OrganAttachmentOperationSystem))]
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
///     Raised on something <see cref="OrganAttachmentOperationComponent"/> to
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
