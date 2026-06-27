using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Mirror;

public sealed class KsMirrorOverlaySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly SystemCollectionHookManager _systemCollectionHookManager = default!;

    private static readonly ProtoId<ShaderPrototype> MirrorShaderId = "KsMirror";
    private static readonly ProtoId<ShaderPrototype> WhiteShaderId = "KsWhite";
    private static readonly ProtoId<ShaderPrototype> StencilMaskId = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilDrawId = "StencilEqualDraw";

    public override void Initialize()
    {
        base.Initialize();
        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        var overlay = new KsMirrorOverlay(
            _prototypeManager.Index(MirrorShaderId).InstanceUnique(),
            _prototypeManager.Index(WhiteShaderId).InstanceUnique(),
            _prototypeManager.Index(StencilMaskId).InstanceUnique(),
            _prototypeManager.Index(StencilDrawId).InstanceUnique()
        );

        dependencyCollection.InjectDependencies(overlay, oneOff: true);
        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<KsMirrorOverlay>();
    }
}
