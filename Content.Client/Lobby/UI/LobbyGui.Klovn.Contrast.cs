using System.Numerics;
using Content.Client._KS14.Lobby;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

// The lobby backdrop shader: dims bright art under the text regions (cvar) and blurs it in character setup.
// One ShaderInstance serves both, since a control has only one ShaderOverride.
public sealed partial class LobbyGui
{
    private static readonly ProtoId<ShaderPrototype> KsContrastShaderId = "KsLobbyContrast";
    private const float KsBlurRadius = 14f;
    private const float KsRegionPaddingPixels = 16f;
    private const float KsFeatherPixels = 64f; // how far past a region's edge the dimming fades out
    private const int KsMaxRegions = 4;

    [Dependency] private IConfigurationManager _configurationManager = default!;

    private readonly Robust.Client.Graphics.StyleBoxEmpty _noPanel = new();
    private ShaderInstance? _contrastShader;
    private Action<bool>? _contrastCVarHandler;
    private bool _contrastEnabled;
    private bool _blurActive;
    private bool _contrastDirty = true;
    private readonly Control[] _contrastRegionControls = new Control[KsMaxRegions];
    private readonly string[] _regionParameters = ["region0", "region1", "region2", "region3"];

    private void KsInitialiseContrast()
    {
        _contrastShader = ProtoMan.Index(KsContrastShaderId).InstanceUnique();

        _contrastRegionControls[0] = KsMenuColumn;
        _contrastRegionControls[1] = CharacterPreview;
        _contrastRegionControls[2] = RightSide;
        _contrastRegionControls[3] = CharacterSetupState; // text over the blurred backdrop in character setup

        // Rects are recomputed lazily in Draw when one of these says the layout moved (see KsMarkContrastDirty).
        OnResized += KsMarkContrastDirty;
        KsViewport.OnResized += KsMarkContrastDirty;
        Background.OnResized += KsMarkContrastDirty;
        foreach (var region in _contrastRegionControls)
        {
            region.OnResized += KsMarkContrastDirty;
        }

        // Visibility notifications reach every descendant, so DefaultState covers the menu column and the preview.
        DefaultState.OnVisibilityChanged += KsMarkContrastDirty;
        CharacterSetupState.OnVisibilityChanged += KsMarkContrastDirty;
        RightSide.OnVisibilityChanged += KsMarkContrastDirty;

        _contrastEnabled = _configurationManager.GetCVar(KsCCVars.LobbyAdaptiveContrast);
        KsApplyContrastShader();
    }

    // A Control has no Subs and the config manager outlives it, so the cvar subscription is held only while the
    //      lobby is in the tree, and is taken off again on the way out (and on Dispose, for one that never entered).
    protected override void EnteredTree()
    {
        base.EnteredTree();

        KsSubscribeContrastCVar();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        KsUnsubscribeContrastCVar();
    }

    private void KsSubscribeContrastCVar()
    {
        if (_contrastCVarHandler != null)
            return;

        _contrastCVarHandler = enabled =>
        {
            _contrastEnabled = enabled;
            KsApplyContrastShader();
        };
        _configurationManager.OnValueChanged(KsCCVars.LobbyAdaptiveContrast, _contrastCVarHandler, invokeImmediately: true);
    }

    private void KsUnsubscribeContrastCVar()
    {
        if (_contrastCVarHandler == null)
            return;

        _configurationManager.UnsubValueChanged(KsCCVars.LobbyAdaptiveContrast, _contrastCVarHandler);
        _contrastCVarHandler = null;
    }

    private void KsSetBlurActive(bool blurActive)
    {
        _blurActive = blurActive;
        KsApplyContrastShader();
    }

    private void KsMarkContrastDirty(Control control)
    {
        _contrastDirty = true;
    }

    private void KsMarkContrastDirty()
    {
        _contrastDirty = true;
    }

    private void KsApplyContrastShader()
    {
        // The constructor sets the camera view before KsInitialiseContrast runs, which applies it again.
        if (_contrastShader == null)
            return;

        // Only the lobby art is dimmed. The camera shows the live game, which the dimming would misrepresent, and
        //      lobby art changes only between rounds, so the regions are all that ever needs recomputing.
        var contrast = _contrastEnabled && !KsViewportContainer.Visible;
        var active = contrast || _blurActive;
        var shader = active ? _contrastShader : null;
        Background.ShaderOverride = shader;
        KsViewport.KsDrawShader = shader;

        // The dimming already darkens the chat column, so the right-edge fade would stack on it as a visible stripe.
        RightSide.PanelOverride = contrast ? _noPanel : null;

        _contrastShader.SetParameter("blurRadius", _blurActive ? KsBlurRadius : 0f);
        // With the cvar off the regions are cleared, leaving a pure blur.
        _contrastShader.SetParameter("strength", contrast ? 1f : 0f);
        _contrastDirty = true;
    }

    private void KsRecomputeContrastRegions()
    {
        if (_contrastShader == null)
            return;

        UIBox2 quadPixels;
        if (KsViewportContainer.Visible)
        {
            var drawBox = KsViewport.KsGetDrawBox();
            if (drawBox == null)
                return; // The viewport is created on its first draw; stay dirty and retry next frame.

            quadPixels = UIBox2.FromDimensions(
                (Vector2)KsViewport.GlobalPixelPosition + (Vector2)drawBox.Value.TopLeft,
                (Vector2)drawBox.Value.Size);
        }
        else
        {
            quadPixels = UIBox2.FromDimensions((Vector2)Background.GlobalPixelPosition, (Vector2)Background.PixelSize);
        }

        _contrastDirty = false;
        _contrastShader.SetParameter("quadPixelSize", quadPixels.Size);
        _contrastShader.SetParameter("feather", KsFeatherPixels * UIScale);
        var padding = KsRegionPaddingPixels * UIScale;
        for (var i = 0; i < KsMaxRegions; i++)
        {
            var region = _contrastRegionControls[i];
            var mapped = Vector4.Zero;
            if (region.VisibleInTree)
            {
                var regionPixels = UIBox2.FromDimensions((Vector2)region.GlobalPixelPosition, (Vector2)region.PixelSize);
                mapped = KsLobbyContrastMath.RegionToQuadSpace(regionPixels, quadPixels, padding);
            }

            _contrastShader.SetParameter(_regionParameters[i], mapped);
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        // Runs after layout, and only does work when a region or the backdrop changed.
        if (_contrastDirty)
            KsRecomputeContrastRegions();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            KsUnsubscribeContrastCVar();

            Background.ShaderOverride = null;
            KsViewport.KsDrawShader = null;
            _contrastShader?.Dispose();
        }

        base.Dispose(disposing);
    }
}
