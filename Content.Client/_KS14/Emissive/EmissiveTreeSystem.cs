using System.Numerics;
using Content.Shared._KS14.Emissive;
using Robust.Client.GameObjects;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Containers;
using Robust.Shared.Physics;

namespace Content.Client._KS14.Emissive;

/// <summary>
///     Keeps a tree of the entities with emissive layers, so <see cref="EmissiveOverlay"/> finds the ones in view without
///         walking every entity there.
/// </summary>
public sealed partial class EmissiveTreeSystem : ComponentTreeSystem<EmissiveTreeComponent, EmissiveLayersComponent>
{
    [Dependency] private SpriteSystem _spriteSystem = default!;

    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    protected override bool DoFrameUpdate => true;
    protected override bool DoTickUpdate => false;
    protected override bool Recursive => false;

    protected override Box2 ExtractAabb(in ComponentTreeEntry<EmissiveLayersComponent> entry, Vector2 pos, Angle rot)
    {
        // The glow reaches past the sprite by its radius, and the whole of it can be offset.
        var glowReach = entry.Component.GlowRadius + entry.Component.Offset.Length();
        foreach (var layerParams in entry.Component.LayerParams.Values)
            glowReach = MathF.Max(glowReach, (layerParams.GlowRadius ?? 0f) + entry.Component.Offset.Length());

        if (!_spriteQuery.TryComp(entry.Uid, out var spriteComponent))
            return new Box2(pos, pos).Enlarged(glowReach);

        return _spriteSystem.CalculateBounds((entry.Uid, spriteComponent), pos, rot, default).CalcBoundingBox().Enlarged(glowReach);
    }

    [SubscribeLocalEvent]
    private void OnInit(Entity<EmissiveLayersComponent> entity, ref ComponentInit args)
    {
        // Read when the tree first adds it, which happens after this.
        entity.Comp.InContainer = (MetaData(entity.Owner).Flags & MetaDataFlags.InContainer) != 0;
    }

    // By-value container messages, so the (uid, component, args) form: the engine has no Entity<T> handler for them.
    [SubscribeLocalEvent]
    private void OnInsertedIntoContainer(EntityUid uid, EmissiveLayersComponent component, EntGotInsertedIntoContainerMessage args)
    {
        SetInContainer((uid, component), inContainer: true);
    }

    [SubscribeLocalEvent]
    private void OnRemovedFromContainer(EntityUid uid, EmissiveLayersComponent component, EntGotRemovedFromContainerMessage args)
    {
        SetInContainer((uid, component), inContainer: false);
    }

    private void SetInContainer(Entity<EmissiveLayersComponent> entity, bool inContainer)
    {
        if (entity.Comp.InContainer == inContainer)
            return;

        entity.Comp.InContainer = inContainer;
        QueueTreeUpdate(entity.Owner, entity.Comp);
    }
}
