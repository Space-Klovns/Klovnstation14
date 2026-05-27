using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Explosion.Shockwave;

public sealed class KsShockwaveOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SystemCollectionHookManager _systemCollectionHookManager = default!;

    private static readonly ProtoId<ShaderPrototype> ShaderPrototype = "KsShockwave";

    public override void Initialize()
    {
        base.Initialize();
        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        var shockwaveOverlay = new KsShockwaveOverlay(_prototypeManager.Index(ShaderPrototype).InstanceUnique());
        dependencyCollection.InjectDependencies(shockwaveOverlay, oneOff: true);

        _overlayManager.AddOverlay(shockwaveOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<KsShockwaveOverlay>();
    }
}
