using Robust.Shared.ComponentTrees;
using Robust.Shared.Physics;

namespace Content.Client._KS14.Light;

/// <summary>
///     A map's or grid's tree of <see cref="KsAmbientOcclusionCasterComponent"/>s.
/// </summary>
[RegisterComponent]
public sealed partial class KsAmbientOcclusionCasterTreeComponent : Component, IComponentTreeComponent<KsAmbientOcclusionCasterComponent>
{
    public DynamicTree<ComponentTreeEntry<KsAmbientOcclusionCasterComponent>> Tree { get; set; } = default!;
}
