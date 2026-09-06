using Content.Shared._KS14.SupplyPod;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.SupplyPod;

/// <summary>
///     Drives <see cref="SupplyPodDrawDepthComponent"/>.
/// </summary>
/// <remarks>
///     The launch/land events do the work whenever the pod is actually being watched. They only
///     fire while the pod is in PVS though, so <see cref="SupplyPodComponent.Landed"/> is used as
///     the authoritative fallback for pods that were launched or landed out of view.
/// </remarks>
public sealed partial class SupplyPodDrawDepthSystem : EntitySystem
{
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<SupplyPodComponent> _supplyPodQuery = default!;
    [Dependency] private EntityQuery<SupplyPodDrawDepthComponent> _drawDepthQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupplyPodDrawDepthComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SupplyPodDrawDepthComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<SupplyPodDrawDepthComponent, SupplyPodLaunchedEvent>(OnLaunched);
        SubscribeLocalEvent<SupplyPodDrawDepthComponent, SupplyPodLandedEvent>(OnLanded);

        // A pod that lands, or is launched, outside of PVS never raises those events on this
        // client, so the networked flag has to be able to correct the depth on its own.
        SubscribeLocalEvent<SupplyPodComponent, AfterAutoHandleStateEvent>(OnSupplyPodState);
    }

    private void OnStartup(Entity<SupplyPodDrawDepthComponent> entity, ref ComponentStartup args)
    {
        if (!_spriteQuery.TryComp(entity.Owner, out var spriteComponent))
            return;

        entity.Comp.OriginalDrawDepth ??= spriteComponent.DrawDepth;

        if (!_supplyPodQuery.TryComp(entity.Owner, out var supplyPodComponent))
            return;

        ApplyDrawDepth((entity.Owner, entity.Comp, spriteComponent), supplyPodComponent.Landed);
    }

    private void OnShutdown(Entity<SupplyPodDrawDepthComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.OriginalDrawDepth is not { } originalDrawDepth
            || !_spriteQuery.TryComp(entity.Owner, out var spriteComponent))
            return;

        _spriteSystem.SetDrawDepth((entity.Owner, spriteComponent), originalDrawDepth);
    }

    private void OnLaunched(Entity<SupplyPodDrawDepthComponent> entity, ref SupplyPodLaunchedEvent args)
    {
        SetDrawDepth(entity, landed: false);
    }

    private void OnLanded(Entity<SupplyPodDrawDepthComponent> entity, ref SupplyPodLandedEvent args)
    {
        SetDrawDepth(entity, landed: true);
    }

    private void OnSupplyPodState(Entity<SupplyPodComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        if (!_drawDepthQuery.TryComp(entity.Owner, out var drawDepthComponent))
            return;

        SetDrawDepth((entity.Owner, drawDepthComponent), entity.Comp.Landed);
    }

    private void SetDrawDepth(Entity<SupplyPodDrawDepthComponent> entity, bool landed)
    {
        if (!_spriteQuery.TryComp(entity.Owner, out var spriteComponent))
            return;

        ApplyDrawDepth((entity.Owner, entity.Comp, spriteComponent), landed);
    }

    private void ApplyDrawDepth(Entity<SupplyPodDrawDepthComponent, SpriteComponent> entity, bool landed)
    {
        var (uid, drawDepthComponent, spriteComponent) = entity;

        // The sprite's authored depth stands in for the landed depth, so a pod that only wants to
        // fly above things doesn't have to restate where it belongs on the ground.
        drawDepthComponent.OriginalDrawDepth ??= spriteComponent.DrawDepth;

        var drawDepth = landed
            ? (int?)drawDepthComponent.LandedDrawDepth ?? drawDepthComponent.OriginalDrawDepth.Value
            : (int)drawDepthComponent.TransitDrawDepth;

        if (spriteComponent.DrawDepth == drawDepth)
            return;

        _spriteSystem.SetDrawDepth((uid, spriteComponent), drawDepth);
    }
}
