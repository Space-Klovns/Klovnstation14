using Content.Shared._KS14.IoC;
using Content.Shared._KS14.SupplyPod;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.SupplyPod;

public sealed partial class SupplyPodSystem : SharedSupplyPodSystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _hookManager = default!;
    [Dependency] private SupplyPodDescentSystem _supplyPodDescentSystem = default!;

    private static readonly ProtoId<ShaderPrototype> StencilMaskShaderId = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilDrawShaderId = "StencilDraw";

    public override void Initialize()
    {
        base.Initialize();

        _hookManager.HookAction(OnDependencyAvailable);

        // A launched pod turns around mid-air and starts a second leg without the component ever
        // being removed, so component startup alone does not cover every flight.
        SubscribeLocalEvent<ActiveSupplyPodComponent, AfterAutoHandleStateEvent>(OnActiveState);
    }

    protected override void OnActiveStartup(Entity<ActiveSupplyPodComponent> entity, ref ComponentStartup args)
    {
        base.OnActiveStartup(entity, ref args);
        _supplyPodDescentSystem.DoStartup(entity);
    }

    protected override void OnActiveShutdown(Entity<ActiveSupplyPodComponent> entity, ref ComponentShutdown args)
    {
        base.OnActiveShutdown(entity, ref args);
        _supplyPodDescentSystem.DoShutdown(entity);
    }

    private void OnActiveState(Entity<ActiveSupplyPodComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        _supplyPodDescentSystem.DoStartup(entity);
    }

    private void OnDependencyAvailable(IDependencyCollection dependencyCollection)
    {
        var overlay = new SupplyPodOverlay(
            _prototypeManager.Index(StencilMaskShaderId).InstanceUnique(),
            _prototypeManager.Index(StencilDrawShaderId).InstanceUnique()
        );

        dependencyCollection.InjectDependencies(overlay, oneOff: true);
        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<SupplyPodOverlay>();
    }
}
