using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.WetOverlay;

public sealed class KsWetOverlaySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly SystemCollectionHookManager _systemCollectionHookManager = default!;

    private static readonly ProtoId<ShaderPrototype> ShaderId = "KsScreenRain";

    public override void Initialize()
    {
        base.Initialize();
        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        var overlay = new KsWetOverlay(_prototypeManager.Index(ShaderId).InstanceUnique());

        dependencyCollection.InjectDependencies(overlay, oneOff: true);
        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<KsWetOverlay>();
    }
}
