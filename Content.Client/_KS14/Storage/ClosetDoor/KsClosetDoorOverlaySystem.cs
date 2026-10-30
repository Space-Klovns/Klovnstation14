using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.IoC;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Storage.ClosetDoor;

public sealed partial class KsClosetDoorOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;

    private static readonly ProtoId<ShaderPrototype> MaskShader = "KsStencilMaskSecondBit";
    private static readonly ProtoId<ShaderPrototype> ClearShader = "KsStencilClearSecondBit";
    private static readonly ProtoId<ShaderPrototype> DrawShader = "KsStencilSecondBitDraw";
    private static readonly ProtoId<ShaderPrototype> SnapShader = "KsSkewPixelSnap";
    private static readonly ProtoId<ShaderPrototype> SnapUnshadedShader = "KsSkewPixelSnapUnshaded";

    private KsClosetDoorFrontOverlay? _frontOverlay;
    private KsClosetDoorBackOverlay? _backOverlay;
    private bool _pixelSnap;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.ClosetDoorPixelSnap, SetPixelSnap, invokeImmediately: true);
        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        // Indexed once here: the overlays make an instance per draw from these.
        var snapShader = ProtoMan.Index(SnapShader);
        var snapUnshadedShader = ProtoMan.Index(SnapUnshadedShader);

        _frontOverlay = new KsClosetDoorFrontOverlay(snapShader, snapUnshadedShader);
        _backOverlay = new KsClosetDoorBackOverlay(
            snapShader,
            snapUnshadedShader,
            ProtoMan.Index(MaskShader).Instance(),
            ProtoMan.Index(ClearShader).Instance(),
            ProtoMan.Index(DrawShader).Instance());

        AddOverlay(dependencyCollection, _frontOverlay);
        AddOverlay(dependencyCollection, _backOverlay);
        SetPixelSnap(_pixelSnap);
    }

    private void AddOverlay(IDependencyCollection dependencyCollection, KsClosetDoorOverlay overlay)
    {
        dependencyCollection.InjectDependencies(overlay, oneOff: true);
        _overlayManager.AddOverlay(overlay);
    }

    private void SetPixelSnap(bool pixelSnap)
    {
        _pixelSnap = pixelSnap;

        if (_frontOverlay != null)
            _frontOverlay.PixelSnap = pixelSnap;

        if (_backOverlay != null)
            _backOverlay.PixelSnap = pixelSnap;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<KsClosetDoorFrontOverlay>();
        _overlayManager.RemoveOverlay<KsClosetDoorBackOverlay>();
        _frontOverlay = null;
        _backOverlay = null;
    }
}
