using Robust.Shared.ComponentTrees;
using Robust.Shared.Physics;

namespace Content.Client._KS14.Light;

/// <summary>
///     Marks an entity that can cast ambient occlusion, and is its entry in the casters' tree. Added by
///         <see cref="KsAmbientOcclusionCasterSystem"/> when the entity initialises, to entities that pass the checks that
///         don't change at runtime; the AO overlay checks the rest each frame.
/// </summary>
[RegisterComponent]
public sealed partial class KsAmbientOcclusionCasterComponent : Component, IComponentTreeEntry<KsAmbientOcclusionCasterComponent>
{
    /// <summary>
    ///     Whether this is an item, which can rest on a surface and cast onto it.
    /// </summary>
    [ViewVariables, Access(typeof(KsAmbientOcclusionCasterSystem))]
    public bool IsItem;

    /// <summary>
    ///     Whether this has a <see cref="Content.Shared.Placeable.PlaceableSurfaceComponent"/>, which items can cast onto.
    /// </summary>
    [ViewVariables, Access(typeof(KsAmbientOcclusionCasterSystem))]
    public bool IsSurface;

    /// <summary>
    ///     Whether the entity is in a container, and so out of the tree.
    /// </summary>
    [ViewVariables, Access(typeof(KsAmbientOcclusionCasterSystem))]
    public bool InContainer;

    public EntityUid? TreeUid { get; set; }

    public DynamicTree<ComponentTreeEntry<KsAmbientOcclusionCasterComponent>>? Tree { get; set; }

    public bool AddToTree => !InContainer;

    public bool TreeUpdateQueued { get; set; }
}
