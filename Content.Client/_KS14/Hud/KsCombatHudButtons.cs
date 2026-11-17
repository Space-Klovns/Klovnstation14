using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Resist;
using Content.Shared._KS14.ThrowMode;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Hud;

/// <summary>
///     tgstation's throw and resist buttons, stacked beside the hotbar: throw mode on top, lit while it is on, and
///         resist below, lit while pressed. Hidden for anything without throw mode, such as a ghost or a borg, and
///         for players who turn them off in the options (<see cref="KsCCVars.CombatHudButtons"/>).
/// </summary>
public sealed partial class KsCombatHudButtons : BoxContainer
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly ResPath Rsi = new("_KS14/Interface/Hud/combat_buttons.rsi");

    /// <summary>
    ///     Width of each icon in the RSI. Each is half as tall, so two stacked make a square.
    /// </summary>
    private const int IconWidth = 32;

    public TextureButton ThrowButton { get; }

    public TextureButton ResistButton { get; }

    /// <summary>
    ///     How long the resist button stays lit after a click, so that a quick one still shows.
    /// </summary>
    private static readonly TimeSpan ResistMinLitTime = TimeSpan.FromSeconds(0.2);

    private bool? _shownThrowEnabled;

    private bool? _shownResistLit;

    private TimeSpan _resistLitUntil;

    public KsCombatHudButtons()
    {
        IoCManager.InjectDependencies(this);

        Orientation = LayoutOrientation.Vertical;
        VerticalAlignment = VAlignment.Bottom;
        SeparationOverride = 0;

        // The two 32x16 icons, scaled so that together they are exactly one inventory slot.
        var scale = new Vector2(SlotControl.DefaultButtonSize / (float)IconWidth);

        ThrowButton = new TextureButton
        {
            ToolTip = Loc.GetString("ks-hud-throw-tooltip"),
            HorizontalAlignment = HAlignment.Center,
            Scale = scale,
        };
        ThrowButton.OnPressed += _ => _entityManager.RaisePredictiveEvent(new KsToggleThrowModeEvent());
        AddChild(ThrowButton);

        ResistButton = new TextureButton
        {
            ToolTip = Loc.GetString("ks-hud-resist-tooltip"),
            HorizontalAlignment = HAlignment.Center,
            Scale = scale,
        };
        ResistButton.OnButtonDown += _ => _resistLitUntil = _timing.RealTime + ResistMinLitTime;
        ResistButton.OnPressed += _ => _entityManager.RaisePredictiveEvent(new KsResistRequestEvent());
        AddChild(ResistButton);

        UpdateThrowButton(false);
        UpdateResistButton(false);
        SetButtonsVisible(false);
    }

    // The configuration manager outlives the game screen, so the subscription lasts only while this is on it.
    protected override void EnteredTree()
    {
        base.EnteredTree();
        _configurationManager.OnValueChanged(KsCCVars.CombatHudButtons, OnShownChanged, true);
    }

    protected override void ExitedTree()
    {
        _configurationManager.UnsubValueChanged(KsCCVars.CombatHudButtons, OnShownChanged);
        base.ExitedTree();
    }

    // The option hides the whole column, so the hotbar closes up behind it. Hidden, it gets no frame updates, which
    //      is fine: only this brings it back.
    private void OnShownChanged(bool shown)
    {
        Visible = shown;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        UpdateResistButton(ResistButton.DrawMode == BaseButton.DrawModeEnum.Pressed || _timing.RealTime < _resistLitUntil);

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
        ThrowButton.Visible = visible;
        ResistButton.Visible = visible;
    }

    private void UpdateThrowButton(bool enabled)
    {
        if (_shownThrowEnabled == enabled)
            return;

        _shownThrowEnabled = enabled;
        ThrowButton.TextureNormal = GetTexture(enabled ? "throw_on" : "throw_off");
    }

    private void UpdateResistButton(bool lit)
    {
        if (_shownResistLit == lit)
            return;

        _shownResistLit = lit;
        ResistButton.TextureNormal = GetTexture(lit ? "resist_on" : "resist_off");
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
