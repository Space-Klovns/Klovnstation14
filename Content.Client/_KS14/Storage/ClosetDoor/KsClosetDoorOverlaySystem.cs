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

    private static readonly ProtoId<ShaderPrototype> ResetShader = "KsStencilResetSecondBit";
    private static readonly ProtoId<ShaderPrototype> MaskShader = "KsStencilMaskSecondBit";
    private static readonly ProtoId<ShaderPrototype> ClearShader = "KsStencilClearSecondBit";
    private static readonly ProtoId<ShaderPrototype> DrawShader = "KsStencilSecondBitDraw";
    private static readonly ProtoId<ShaderPrototype> SnapShader = "KsSkewPixelSnap";
    private static readonly ProtoId<ShaderPrototype> SnapUnshadedShader = "KsSkewPixelSnapUnshaded";
    private static readonly ProtoId<ShaderPrototype> OcclusionResetShader = "KsStencilResetThirdBit";
    private static readonly ProtoId<ShaderPrototype> OcclusionMaskShader = "KsStencilMaskThirdBit";
    private static readonly ProtoId<ShaderPrototype> OcclusionClearShader = "KsStencilClearThirdBit";
    private static readonly ProtoId<ShaderPrototype> OcclusionDrawShader = "KsStencilOutsideThirdBitDraw";
    private static readonly ProtoId<ShaderPrototype> OcclusionSnapShader = "KsSkewPixelSnapOutsideThirdBit";

    private KsClosetDoorFrontOverlay? _frontOverlay;
    private KsClosetDoorBackOverlay? _backOverlay;
    private bool _pixelSnap;
    private bool _debugMask;
    private bool _occlusionMask;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.ClosetDoorPixelSnap, SetPixelSnap, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.ClosetDoorDebugMask, SetDebugMask, invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.ClosetDoorOcclusionMask, SetOcclusionMask, invokeImmediately: true);
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
            ProtoMan.Index(ResetShader).Instance(),
            ProtoMan.Index(MaskShader).Instance(),
            ProtoMan.Index(ClearShader).Instance(),
            ProtoMan.Index(DrawShader).Instance(),
            new KsClosetDoorOcclusionShaders(
                ProtoMan.Index(OcclusionResetShader).Instance(),
                ProtoMan.Index(OcclusionMaskShader).Instance(),
                ProtoMan.Index(OcclusionClearShader).Instance(),
                ProtoMan.Index(OcclusionDrawShader).Instance(),
                ProtoMan.Index(OcclusionSnapShader)));

        AddOverlay(dependencyCollection, _frontOverlay);
        AddOverlay(dependencyCollection, _backOverlay);
        SetPixelSnap(_pixelSnap);
        SetDebugMask(_debugMask);
        SetOcclusionMask(_occlusionMask);
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

    private void SetDebugMask(bool debugMask)
    {
        _debugMask = debugMask;

        if (_backOverlay != null)
            _backOverlay.DebugMask = debugMask;
    }

    private void SetOcclusionMask(bool occlusionMask)
    {
        _occlusionMask = occlusionMask;

        if (_backOverlay != null)
            _backOverlay.OcclusionMask = occlusionMask;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        // Removing an overlay doesn't dispose it, and these own the snapping shaders' unique instances.
        _overlayManager.RemoveOverlay<KsClosetDoorFrontOverlay>();
        _overlayManager.RemoveOverlay<KsClosetDoorBackOverlay>();
        _frontOverlay?.Dispose();
        _backOverlay?.Dispose();
        _frontOverlay = null;
        _backOverlay = null;
    }
}
