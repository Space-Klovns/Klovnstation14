using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Containers;
using Robust.Shared.Physics;

namespace Content.Client._KS14.Storage.ClosetDoor;

/// <summary>
///     Keeps a tree of the closets whose door is drawn, so <see cref="KsClosetDoorOverlay"/> finds the ones in view
///         without walking every closet there. A closet is in it only while its door is open or swinging
///         (<see cref="KsClosetDoorVisualsComponent.AddToTree"/>).
/// </summary>
public sealed partial class KsClosetDoorTreeSystem : ComponentTreeSystem<KsClosetDoorTreeComponent, KsClosetDoorVisualsComponent>
{
    [Dependency] private SpriteSystem _spriteSystem = default!;

    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    /// <summary>
    ///     How far past the sprite's bounds a swung door can reach: tgstation's doors swing out past the frame by a
    ///         couple of pixels to the side, and drop by a couple below it.
    /// </summary>
    private const float DoorReach = 0.25f;

    protected override bool DoFrameUpdate => true;
    protected override bool DoTickUpdate => false;
    protected override bool Recursive => false;

    protected override Box2 ExtractAabb(in ComponentTreeEntry<KsClosetDoorVisualsComponent> entry, Vector2 pos, Angle rot)
    {
        if (!_spriteQuery.TryComp(entry.Uid, out var spriteComponent))
            return new Box2(pos, pos).Enlarged(DoorReach);

        return _spriteSystem.CalculateBounds((entry.Uid, spriteComponent), pos, rot, default).CalcBoundingBox().Enlarged(DoorReach);
    }

    [SubscribeLocalEvent]
    private void OnInit(Entity<KsClosetDoorVisualsComponent> entity, ref ComponentInit args)
    {
        // Read when the tree first adds it, which happens after this.
        entity.Comp.InContainer = (MetaData(entity.Owner).Flags & MetaDataFlags.InContainer) != 0;
    }

    // By-value container messages, so the (uid, component, args) form: the engine has no Entity<T> handler for them.
    [SubscribeLocalEvent]
    private void OnInsertedIntoContainer(EntityUid uid, KsClosetDoorVisualsComponent component, EntGotInsertedIntoContainerMessage args)
    {
        SetInContainer((uid, component), inContainer: true);
    }

    [SubscribeLocalEvent]
    private void OnRemovedFromContainer(EntityUid uid, KsClosetDoorVisualsComponent component, EntGotRemovedFromContainerMessage args)
    {
        SetInContainer((uid, component), inContainer: false);
    }

    private void SetInContainer(Entity<KsClosetDoorVisualsComponent> entity, bool inContainer)
    {
        if (entity.Comp.InContainer == inContainer)
            return;

        entity.Comp.InContainer = inContainer;
        QueueTreeUpdate(entity.Owner, entity.Comp);
    }
}
