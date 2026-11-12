using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Client._KS14.Visor;

/// <summary>
///     Puts the visor overlay up for the session: water from the weather, on the local player's screen.
/// </summary>
public sealed partial class KsVisorSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "KsVisorRain";

    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        // The overlay takes systems, which exist only once every system has been made.
        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        var overlay = new KsVisorOverlay(ProtoMan.Index(ShaderId).InstanceUnique(), _robustRandom);
        dependencyCollection.InjectDependencies(overlay, oneOff: true);
        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<KsVisorOverlay>();
    }
}
