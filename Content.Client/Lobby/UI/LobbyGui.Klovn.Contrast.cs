using System.Numerics;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

// The lobby backdrop shader: a film grade over the lobby art (vignette, darkening, fringe, scanlines), behind the cvar
//      klovn.lobby.adaptive_contrast. It replaced the per-region dimming of the tactical layout. The backdrop is never blurred.
public sealed partial class LobbyGui
{
    private static readonly ProtoId<ShaderPrototype> KsGradeShaderId = "KsLobbyGrade";

    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private ShaderInstance? _contrastShader;
    private Action<bool>? _contrastCVarHandler;
    private bool _contrastEnabled;
    private bool _contrastDirty = true;

    private void KsInitialiseContrast()
    {
        _contrastShader = _prototypeManager.Index(KsGradeShaderId).InstanceUnique();

        OnResized += KsMarkContrastDirty;
        Background.OnResized += KsMarkContrastDirty;

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

    private void KsMarkContrastDirty()
    {
        _contrastDirty = true;
    }

    private void KsApplyContrastShader()
    {
        // The constructor sets the camera view before KsInitialiseContrast runs, which applies it again.
        if (_contrastShader == null)
            return;

        // Only the lobby art is graded. The camera shows the live game, which the grade would misrepresent.
        var grade = _contrastEnabled && !KsViewportContainer.Visible;
        var shader = grade ? _contrastShader : null;
        Background.ShaderOverride = shader;
        KsViewport.KsDrawShader = shader;

        _contrastShader.SetParameter("strength", grade ? 1f : 0f);
        _contrastDirty = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        // Runs after layout, and only does work when the backdrop changed size.
        if (!_contrastDirty || _contrastShader == null)
            return;

        _contrastDirty = false;
        _contrastShader.SetParameter("quadPixelSize", (Vector2)Background.PixelSize);
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
