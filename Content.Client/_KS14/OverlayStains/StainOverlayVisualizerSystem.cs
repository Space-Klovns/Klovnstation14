using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.OverlayStains;

public sealed partial class StainOverlayVisualizerSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    private StainOverlay _stainOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        _stainOverlay = new();
        dependencyCollection.InjectDependencies(_stainOverlay, oneOff: true);
        _stainOverlay.Initialise();

        _overlayManager.AddOverlay(_stainOverlay);

        // Subs.CVar rather than subscribing from the overlay, so the subscriptions go when this system does.
        Subs.CVar(_configurationManager, KsCCVars.ComplexStainDrawing, complexDrawing => _stainOverlay.ComplexDrawing = complexDrawing, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.StainArcDegrees, arcDegrees => _stainOverlay.StainArc = Angle.FromDegrees(arcDegrees), invokeImmediately: true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<StainOverlay>();
    }
}
