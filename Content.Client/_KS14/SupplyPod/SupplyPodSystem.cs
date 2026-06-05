using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.SupplyPod;

public sealed class SupplyPodSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly SystemCollectionHookManager _hookManager = default!;

    private static readonly ProtoId<ShaderPrototype> ShaderId = "KsCutout";

    public override void Initialize()
    {
        base.Initialize();

        _hookManager.HookAction(OnDependencyAvailable);
    }

    private void OnDependencyAvailable(IDependencyCollection dependencyCollection)
    {
        var shader = _prototypeManager.Index(ShaderId).InstanceUnique();
        var overlay = new SupplyPodOverlay(shader);

        dependencyCollection.InjectDependencies(overlay, oneOff: true);
        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<SupplyPodOverlay>();
    }
}
