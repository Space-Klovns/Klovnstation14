using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.IoC;

namespace Content.Client._KS14.Particles;

/// <summary>
///     Puts one <see cref="KsParticlesOverlay"/> per layer into the overlay manager, with the system dependency
///         collection injected, and takes them out again.
/// </summary>
public sealed partial class KsParticlesOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        AddOverlay(dependencyCollection, new KsParticlesFloorOverlay());
        AddOverlay(dependencyCollection, new KsParticlesBelowMobsOverlay());
        AddOverlay(dependencyCollection, new KsParticlesEffectsOverlay());
        AddOverlay(dependencyCollection, new KsParticlesTopOverlay());
    }

    private void AddOverlay(IDependencyCollection dependencyCollection, KsParticlesOverlay overlay)
    {
        dependencyCollection.InjectDependencies(overlay, oneOff: true);
        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<KsParticlesFloorOverlay>();
        _overlayManager.RemoveOverlay<KsParticlesBelowMobsOverlay>();
        _overlayManager.RemoveOverlay<KsParticlesEffectsOverlay>();
        _overlayManager.RemoveOverlay<KsParticlesTopOverlay>();
    }
}
