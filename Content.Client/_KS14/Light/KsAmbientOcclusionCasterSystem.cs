using System.Numerics;
using Content.Client.Effects;
using Content.Shared._KS14.Light;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Placeable;
using Content.Shared.Projectiles;
using Robust.Client.GameObjects;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._KS14.Light;

/// <summary>
///     Keeps a tree of the entities that can cast ambient occlusion, so the AO overlay queries only those, instead of every
///         sprite in view and then filtering most of them out every frame.
/// </summary>
/// <remarks>
///     What can't change at runtime is decided once, when an entity initialises: entities that fail it never get a
///         <see cref="KsAmbientOcclusionCasterComponent"/>. What can change (visibility, the sprite's layers, draw depth)
///         is left to the overlay, which checks it each frame for the casters it draws.
/// </remarks>
public sealed partial class KsAmbientOcclusionCasterSystem
    : ComponentTreeSystem<KsAmbientOcclusionCasterTreeComponent, KsAmbientOcclusionCasterComponent>
{
    /// <summary>
    ///     Anything drawn this low lies flat on the floor (catwalks, water, puddles, pipes), so casts nothing.
    /// </summary>
    public const int MaxFlatDrawDepth = (int)DrawDepth.HighFloorObjects;

    /// <summary>
    ///     Gases, effects and above aren't solid.
    /// </summary>
    public const int MinEffectDrawDepth = (int)DrawDepth.Gasses;

    [Dependency] private SpriteSystem _spriteSystem = default!;

    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<OccluderComponent> _occluderQuery = default!;
    [Dependency] private EntityQuery<EffectVisualsComponent> _effectVisualsQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;
    [Dependency] private EntityQuery<KsNoAmbientOcclusionComponent> _noAmbientOcclusionQuery = default!;
    [Dependency] private EntityQuery<KsForceAmbientOcclusionComponent> _forceAmbientOcclusionQuery = default!;
    [Dependency] private EntityQuery<ItemComponent> _itemQuery = default!;
    [Dependency] private EntityQuery<PlaceableSurfaceComponent> _placeableSurfaceQuery = default!;

    protected override bool DoFrameUpdate => true;
    protected override bool DoTickUpdate => false;
    protected override bool Recursive => false;
    protected override int InitialCapacity => 1024;

    public override void Initialize()
    {
        base.Initialize();

        EntityManager.EntityInitialized += OnEntityInitialized;
    }

    public override void Shutdown()
    {
        EntityManager.EntityInitialized -= OnEntityInitialized;

        base.Shutdown();
    }

    protected override Box2 ExtractAabb(in ComponentTreeEntry<KsAmbientOcclusionCasterComponent> entry, Vector2 pos, Angle rot)
    {
        if (!_spriteQuery.TryComp(entry.Uid, out var spriteComponent))
            return new Box2(pos, pos);

        return _spriteSystem.CalculateBounds((entry.Uid, spriteComponent), pos, rot, default).CalcBoundingBox();
    }

    private void OnEntityInitialized(Entity<MetaDataComponent> entity)
    {
        var uid = entity.Owner;
        if (!_spriteQuery.TryComp(uid, out var spriteComponent)
            || spriteComponent.DrawDepth >= MinEffectDrawDepth)
            return;

        var forced = _forceAmbientOcclusionQuery.HasComp(uid);
        if (!forced && spriteComponent.DrawDepth <= MaxFlatDrawDepth)
            return;

        if (_occluderQuery.HasComp(uid)
            || _effectVisualsQuery.HasComp(uid)
            || _mobStateQuery.HasComp(uid)
            || _projectileQuery.HasComp(uid)
            || _noAmbientOcclusionQuery.HasComp(uid))
            return;

        var casterComponent = AddComp<KsAmbientOcclusionCasterComponent>(uid);
        casterComponent.IsItem = _itemQuery.HasComp(uid);
        casterComponent.IsSurface = _placeableSurfaceQuery.HasComp(uid);
        casterComponent.InContainer = (entity.Comp.Flags & MetaDataFlags.InContainer) != 0;
    }

    // By-value container messages, so the (uid, component, args) form: the engine has no Entity<T> handler for them.
    [SubscribeLocalEvent]
    private void OnInsertedIntoContainer(EntityUid uid, KsAmbientOcclusionCasterComponent component, EntGotInsertedIntoContainerMessage args)
    {
        SetInContainer((uid, component), inContainer: true);
    }

    [SubscribeLocalEvent]
    private void OnRemovedFromContainer(EntityUid uid, KsAmbientOcclusionCasterComponent component, EntGotRemovedFromContainerMessage args)
    {
        SetInContainer((uid, component), inContainer: false);
    }

    private void SetInContainer(Entity<KsAmbientOcclusionCasterComponent> entity, bool inContainer)
    {
        if (entity.Comp.InContainer == inContainer)
            return;

        entity.Comp.InContainer = inContainer;
        QueueTreeUpdate(entity.Owner, entity.Comp);
    }
}
