using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Resist;
using Content.Shared._KS14.ThrowMode;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Hud;

/// <summary>
///     tgstation's throw and resist buttons, stacked beside the hotbar: throw mode on top, lit while it is on, and
///         resist below. Hidden for anything without throw mode, such as a ghost or a borg.
/// </summary>
public sealed partial class KsCombatHudButtons : BoxContainer
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;

    private static readonly ResPath Rsi = new("_KS14/Interface/Hud/combat_buttons.rsi");

    /// <summary>
    ///     Width of each icon in the RSI. Each is half as tall, so two stacked make a square.
    /// </summary>
    private const int IconWidth = 32;

    private readonly TextureButton _throwButton;
    private readonly TextureButton _resistButton;

    public TextureButton ThrowButton => _throwButton;

    public TextureButton ResistButton => _resistButton;

    private bool? _shownThrowEnabled;

    public KsCombatHudButtons()
    {
        IoCManager.InjectDependencies(this);

        Orientation = LayoutOrientation.Vertical;
        VerticalAlignment = VAlignment.Bottom;
        SeparationOverride = 0;

        // The two 32x16 icons, scaled so that together they are exactly one inventory slot.
        var scale = new Vector2(SlotControl.DefaultButtonSize / (float)IconWidth);

        _throwButton = new TextureButton
        {
            ToolTip = Loc.GetString("ks-hud-throw-tooltip"),
            HorizontalAlignment = HAlignment.Center,
            Scale = scale,
        };
        _throwButton.OnPressed += _ => _entityManager.RaisePredictiveEvent(new KsToggleThrowModeEvent());
        AddChild(_throwButton);

        _resistButton = new TextureButton
        {
            ToolTip = Loc.GetString("ks-hud-resist-tooltip"),
            HorizontalAlignment = HAlignment.Center,
            TextureNormal = GetTexture("resist"),
            Scale = scale,
        };
        _resistButton.OnPressed += _ => _entityManager.RaisePredictiveEvent(new KsResistRequestEvent());
        AddChild(_resistButton);

        UpdateThrowButton(false);
        SetButtonsVisible(false);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // Only the buttons hide: a hidden control gets no frame updates, so it could never show itself again.
        if (_playerManager.LocalEntity is not { } player ||
            !_entityManager.TryGetComponent<KsThrowModeComponent>(player, out var throwMode))
        {
            SetButtonsVisible(false);
            return;
        }

        SetButtonsVisible(true);
        UpdateThrowButton(throwMode.Enabled);
    }

    private void SetButtonsVisible(bool visible)
    {
        _throwButton.Visible = visible;
        _resistButton.Visible = visible;
    }

    private void UpdateThrowButton(bool enabled)
    {
        if (_shownThrowEnabled == enabled)
            return;

        _shownThrowEnabled = enabled;
        _throwButton.TextureNormal = GetTexture(enabled ? "throw_on" : "throw_off");
    }

    /// <summary>
    ///     Read from the resource cache: the game screen is built before entity systems exist.
    /// </summary>
    private Texture GetTexture(string state)
    {
        var rsi = _resourceCache.GetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / Rsi).RSI;
        return rsi.TryGetState(state, out var rsiState) ? rsiState.Frame0 : Texture.Transparent;
    }
}
