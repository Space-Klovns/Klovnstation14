using Content.Shared._KS14.Emissive;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Physics;

namespace Content.Client._KS14.Emissive;

/// <summary>
///     A map's or grid's tree of <see cref="EmissiveLayersComponent"/>s.
/// </summary>
[RegisterComponent]
public sealed partial class EmissiveTreeComponent : Component, IComponentTreeComponent<EmissiveLayersComponent>
{
    public DynamicTree<ComponentTreeEntry<EmissiveLayersComponent>> Tree { get; set; } = default!;
}
