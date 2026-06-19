using Content.Shared._KS14.IoC;
using Content.Shared._KS14.SupplyPod;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.SupplyPod;

public sealed class SupplyPodSystem : SharedSupplyPodSystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly SystemCollectionHookManager _hookManager = default!;

    private static readonly ProtoId<ShaderPrototype> StencilMaskShaderId = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilDrawShaderId = "StencilDraw";

    public override void Initialize()
    {
        base.Initialize();

        _hookManager.HookAction(OnDependencyAvailable);
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
