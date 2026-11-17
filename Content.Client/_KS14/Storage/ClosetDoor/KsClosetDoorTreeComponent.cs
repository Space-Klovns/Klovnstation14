using Robust.Shared.ComponentTrees;
using Robust.Shared.Physics;

namespace Content.Client._KS14.Storage.ClosetDoor;

/// <summary>
///     A map's or grid's tree of closets whose door is drawn by <see cref="KsClosetDoorOverlay"/>.
/// </summary>
[RegisterComponent]
public sealed partial class KsClosetDoorTreeComponent : Component, IComponentTreeComponent<KsClosetDoorVisualsComponent>
{
    public DynamicTree<ComponentTreeEntry<KsClosetDoorVisualsComponent>> Tree { get; set; } = default!;
}
