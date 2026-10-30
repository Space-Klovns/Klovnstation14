using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Atmos.ChemicalFire;

public sealed partial class ChemicalFireOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        var unshadedShader = ProtoMan.Index(UnshadedShader).Instance();

        AddOverlay(dependencyCollection, new ChemicalFireUnderOverlay(unshadedShader));
        AddOverlay(dependencyCollection, new ChemicalFireOverOverlay(unshadedShader));
    }

    private void AddOverlay(IDependencyCollection dependencyCollection, ChemicalFireOverlay overlay)
    {
        dependencyCollection.InjectDependencies(overlay, oneOff: true);
        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<ChemicalFireUnderOverlay>();
        _overlayManager.RemoveOverlay<ChemicalFireOverOverlay>();
    }
}
