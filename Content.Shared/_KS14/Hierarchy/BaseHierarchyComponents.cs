using Robust.Shared.Containers;

namespace Content.Shared._KS14.Hierarchy;

/// <summary>
///     For hierarchy components where the hierarchy starts from.
/// </summary>
public interface IHierarchyComponent
{
    /// <summary>
    ///     All entities that are parented to or under this entity.
    ///         Emptied when the entity starts being deleted.
    /// </summary>
    List<EntityUid> RecursiveChildUids { get; set; }

    Container Container { get; set; }
}

/// <summary>
///     For components that make up the hierarchy.
/// </summary>
public interface IHierarchyElementComponent
{
    /// <summary>
    ///     The hierarchy entity that owns this entity.
    /// </summary>
    EntityUid? HierarchyUid { get; set; }

    /// <summary>
    ///     Entities that are only directly parented to this entity, and no further below.
    ///         Emptied when the entity starts being deleted.
    /// </summary>
    HashSet<EntityUid> ChildUids { get; set; }

    Container Container { get; set; }
}
