using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Atmos.ChemicalFire;

public sealed partial class ChemicalFireOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        var overlay = new ChemicalFireOverlay(_prototypeManager.Index(UnshadedShader).Instance());
        dependencyCollection.InjectDependencies(overlay, oneOff: true);

        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<ChemicalFireOverlay>();
    }
}
