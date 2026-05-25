using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Client._KS14.OverlayStains;

public sealed class StainOverlayVisualizerSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly SystemCollectionHookManager _systemCollectionHookManager = default!;

    private StainOverlay _stainOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        _stainOverlay = new() { StainSpriteSpecifier = new SpriteSpecifier.Rsi(new ResPath("/Textures/Effects/crayondecals.rsi"), "splatter") };
        dependencyCollection.InjectDependencies(_stainOverlay, oneOff: true);

        _overlayManager.AddOverlay(_stainOverlay);
        _configurationManager.OnValueChanged(KsCCVars.ComplexStainDrawing, (complexDrawing) => _stainOverlay.ComplexDrawing = complexDrawing, invokeImmediately: true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<StainOverlay>();
    }
}
