using Content.Client._KS14.Lobby;
using Content.Client._KS14.Lobby.Kharkiv;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Lobby;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

/// <summary>What the lobby says about the player's readiness, in whichever layout shows.</summary>
public enum KsLobbyReadyStatus : byte
{
    NotReady,
    Ready,
    Join,
}

/// <summary>The lobby layouts, as <see cref="KsCCVars.LobbyLayout"/> names them in <see cref="KsLobbyLayouts"/>.</summary>
public enum KsLobbyLayout : byte
{
    Tactical,
    Kharkiv,
}

// Switching between the tactical layout (this XAML) and the Kharkiv one (KsKharkiv), live, as the server's
//      klovn.lobby.layout says. Controls both layouts show are single instances, moved between them, so every reference
//      held elsewhere (LobbyState, the vote manager, LobbyUIController) stays good and nothing is rebuilt: no state change,
//      no lost chat. Controls only one layout shows stay in it, hidden with it. See Klovn/Docs/lobby.md.
public sealed partial class LobbyGui
{
    /// <summary>
    ///     A control both layouts show: where it sits and how it looks in the tactical layout, recorded once from the XAML,
    ///     and where it goes and how it looks in the Kharkiv one. Only the layouts' own style classes are swapped: a
    ///     control has classes of its own too (every button carries <c>button</c>, which all button rules select on).
    /// </summary>
    private sealed class KsSharedControl(
        Control control,
        Control kharkivSlot,
        string[] tacticalClasses,
        string[] kharkivClasses,
        LocId? kharkivText)
    {
        public readonly Control Control = control;
        public readonly Control TacticalParent = control.Parent!;
        public readonly int TacticalIndex = control.GetPositionInParent();
        public readonly Thickness TacticalMargin = control.Margin;
        public readonly HAlignment TacticalHorizontal = control.HorizontalAlignment;
        public readonly VAlignment TacticalVertical = control.VerticalAlignment;
        public readonly bool TacticalHorizontalExpand = control.HorizontalExpand;
        public readonly bool TacticalVerticalExpand = control.VerticalExpand;
        public readonly string[] TacticalClasses = tacticalClasses;
        public readonly string? TacticalText = (control as Button)?.Text;
        /// <summary>An invisible marker in the Kharkiv layout: the control goes right after it, laid out as it says.</summary>
        public readonly Control KharkivSlot = kharkivSlot;
        public readonly string[] KharkivClasses = kharkivClasses;
        public readonly LocId? KharkivText = kharkivText;
    }

    private readonly List<KsSharedControl> _ksSharedControls = new();
    private KsLobbyLayout _ksLayout = KsLobbyLayout.Tactical;
    private KsLobbyReadyStatus _ksReadyStatus = KsLobbyReadyStatus.NotReady;
    private Action<string>? _ksLayoutCVarHandler;
    private bool _ksRecheckRightSide;

    /// <summary>The layout showing now.</summary>
    public KsLobbyLayout KsLayout => _ksLayout;

    /// <summary>
    ///     Raised when the player asks for character setup, from any of the buttons that lead there in either layout.
    /// </summary>
    public event Action? KsCharacterSetupRequested;

    private void KsInitialiseLayout()
    {
        // Recorded in ascending tactical position, so putting them back in this order restores every index.
        string[] menuButton = [KsLobbyStyleClasses.KsLobbyMenuButton];
        KsShare(ReadyButton, KsKharkiv.ReadySlot, menuButton, [KsKharkivStyleClasses.MenuTitle], "ks-kharkiv-menu-escape");
        KsShare(ObserveButton, KsKharkiv.ObserveSlot, menuButton, [KsKharkivStyleClasses.MenuItem], "ks-kharkiv-menu-trading");
        KsShare(ManifestButton, KsKharkiv.ManifestSlot, menuButton, [KsKharkivStyleClasses.MenuItem], "ks-kharkiv-menu-hideout");
        KsShare(LeaveButton, KsKharkiv.LeaveSlot, menuButton, [KsKharkivStyleClasses.MenuItem], "ks-kharkiv-menu-exit");
        KsShare(StartTimeValue, KsKharkiv.TimerSlot, [KsLobbyStyleClasses.KsLobbyCountdown], [KsKharkivStyleClasses.BannerValue], null);
        KsShare(VoteContainer, KsKharkiv.VoteSlot, [], [], null);
        KsShare(CharacterSetupState, KsKharkiv.SetupSlot, [], [], null);
        _ksSharedControls.Sort((a, b) => a.TacticalIndex.CompareTo(b.TacticalIndex));
        LeftSide.OnResized += KsRecheckRightSide;

        KsKharkiv.CharacterButton.OnPressed += _ => KsRequestCharacterSetup();
        KsKharkiv.CharacterTab.OnPressed += _ => KsRequestCharacterSetup();
        KsKharkiv.OptionsTab.OnPressed += _ => UserInterfaceManager
            .GetUIController<Content.Client.UserInterface.Systems.EscapeMenu.OptionsUIController>().ToggleWindow();

        KsSetLayout(KsParseLayout(_configurationManager.GetCVar(KsCCVars.LobbyLayout)));
    }

    /// <summary>
    ///     Asks for character setup, unless it is open already. Opening it reloads the editor from the saved profile and
    ///     un-readies the player, so a second request (the Kharkiv layout's CHARACTER tab stays on screen during setup)
    ///     would throw away unsaved edits.
    /// </summary>
    internal void KsRequestCharacterSetup()
    {
        if (CharacterSetupState.Visible)
            return;

        KsCharacterSetupRequested?.Invoke();
    }

    private void KsShare(Control control, Control kharkivSlot, string[] tacticalClasses, string[] kharkivClasses, LocId? kharkivText)
    {
        _ksSharedControls.Add(new KsSharedControl(control, kharkivSlot, tacticalClasses, kharkivClasses, kharkivText));
    }

    /// <summary>
    ///     The layout a cvar value shows today. <c>auto</c> follows the client's own calendar, read when the lobby is built
    ///     and when the cvar changes.
    /// </summary>
    private static KsLobbyLayout KsParseLayout(string id)
    {
        return KsLobbyLayouts.Resolve(id, DateTime.Now) == KsLobbyLayouts.Kharkiv
            ? KsLobbyLayout.Kharkiv
            : KsLobbyLayout.Tactical;
    }

    /// <summary>
    ///     Shows <paramref name="layout"/>, moving the shared controls into it. The server decides this for every client
    ///     through the cvar; this is public for tests.
    /// </summary>
    public void KsSetLayout(KsLobbyLayout layout)
    {
        _ksLayout = layout;
        var kharkiv = layout == KsLobbyLayout.Kharkiv;

        foreach (var shared in _ksSharedControls)
        {
            shared.Control.Orphan();
        }

        foreach (var shared in _ksSharedControls)
        {
            var control = shared.Control;
            foreach (var styleClass in kharkiv ? shared.TacticalClasses : shared.KharkivClasses)
            {
                control.StyleClasses.Remove(styleClass);
            }

            if (kharkiv)
            {
                var slot = shared.KharkivSlot;
                slot.Parent!.AddChild(control);
                control.SetPositionInParent(slot.GetPositionInParent() + 1);
                control.Margin = slot.Margin;
                control.HorizontalAlignment = slot.HorizontalAlignment;
                control.VerticalAlignment = slot.VerticalAlignment;
                control.HorizontalExpand = slot.HorizontalExpand;
                control.VerticalExpand = slot.VerticalExpand;
                foreach (var styleClass in shared.KharkivClasses)
                {
                    if (!control.StyleClasses.Contains(styleClass))
                        control.StyleClasses.Add(styleClass);
                }

                if (control is Button button && shared.KharkivText is { } text)
                    button.Text = Loc.GetString(text);
            }
            else
            {
                shared.TacticalParent.AddChild(control);
                control.SetPositionInParent(shared.TacticalIndex);
                control.Margin = shared.TacticalMargin;
                control.HorizontalAlignment = shared.TacticalHorizontal;
                control.VerticalAlignment = shared.TacticalVertical;
                control.HorizontalExpand = shared.TacticalHorizontalExpand;
                control.VerticalExpand = shared.TacticalVerticalExpand;
                foreach (var styleClass in shared.TacticalClasses)
                {
                    if (!control.StyleClasses.Contains(styleClass))
                        control.StyleClasses.Add(styleClass);
                }

                if (control is Button button && shared.TacticalText != null)
                    button.Text = shared.TacticalText;
            }
        }

        // Character setup decides whether to collapse RightSide from LeftSide's width, which a tactical layout hidden
        //      since the lobby opened never had: show it, and decide again once LeftSide has a width (now, or when it is
        //      first laid out).
        if (!kharkiv && CharacterSetupState.Visible)
        {
            RightSide.Visible = true;
            _ksRecheckRightSide = true;
        }

        KsFadeLeft.Visible = !kharkiv;
        KsFadeBottom.Visible = !kharkiv;
        KsRoot.Visible = !kharkiv;
        KsKharkiv.Visible = kharkiv;

        // What each layout shows differently: the ready text, the countdown block, the backdrop shader. Which of menu and
        //      character setup shows needs nothing here: SwitchState keeps both layouts in step.
        KsSetReadyStatus(_ksReadyStatus);
        if (kharkiv)
            StartTime.Visible = false;
        StartTimeValue.Visible = kharkiv || !string.IsNullOrEmpty(StartTimeValue.Text);
        KsApplyContrastShader();
        KsRecheckRightSide();
    }

    /// <summary>
    ///     Says whether the player is ready, not ready, or can join the running round: as the ready button's own text in
    ///     the tactical layout, as a line under the unchanging ESCAPE FROM KLOVN title in the Kharkiv one.
    /// </summary>
    public void KsSetReadyStatus(KsLobbyReadyStatus status)
    {
        _ksReadyStatus = status;
        if (_ksLayout == KsLobbyLayout.Kharkiv)
        {
            ReadyButton.Text = Loc.GetString("ks-kharkiv-menu-escape");
            KsKharkiv.SetReadyStatus(status);
            return;
        }

        ReadyButton.Text = Loc.GetString(status switch
        {
            KsLobbyReadyStatus.Ready => "ks-lobby-ready-ready",
            KsLobbyReadyStatus.Join => "ks-lobby-ready-join",
            _ => "ks-lobby-ready-not-ready",
        });
    }

    /// <summary>
    ///     While a round runs: the tactical layout hides its countdown (StationTime says the round time), the Kharkiv
    ///     banner's TIMER shows how long the round has gone.
    /// </summary>
    public void KsSetRoundElapsed(TimeSpan elapsed)
    {
        if (_ksLayout == KsLobbyLayout.Kharkiv)
        {
            KsSetCountdown(null, $"{(int) elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}");
            return;
        }

        KsSetCountdown(null, null);
    }

    /// <summary>How many players are connected, for the Kharkiv banner. Cheap to call every frame.</summary>
    public void KsSetPlayerCount(int count)
    {
        KsKharkiv.SetPlayerCount(count);
    }

    /// <summary>The build line, bottom left in the Kharkiv layout.</summary>
    public void KsSetVersionText(string text)
    {
        KsKharkiv.SetVersionText(text);
    }

    /// <summary>
    ///     After a switch to the tactical layout during character setup, once LeftSide has a width: collapses RightSide
    ///     if setup needs the room, by the same rule as <see cref="SwitchState"/>. Called by the switch, and by
    ///     LeftSide's first resize when it had none.
    /// </summary>
    private void KsRecheckRightSide()
    {
        if (!_ksRecheckRightSide || LeftSide.PixelWidth <= 0)
            return;

        _ksRecheckRightSide = false;
        if (!CharacterSetupState.Visible || _ksLayout != KsLobbyLayout.Tactical)
            return;

        var actualWidth = (float) UserInterfaceManager.RootControl.PixelWidth;
        var setupWidth = (float) LeftSide.PixelWidth;
        if (1 - setupWidth / actualWidth > 0.30)
            RightSide.Visible = false;
    }

    private void KsSubscribeLayoutCVar()
    {
        if (_ksLayoutCVarHandler != null)
            return;

        _ksLayoutCVarHandler = id =>
        {
            var layout = KsParseLayout(id);
            if (layout != _ksLayout)
                KsSetLayout(layout);
        };
        _configurationManager.OnValueChanged(KsCCVars.LobbyLayout, _ksLayoutCVarHandler, invokeImmediately: true);
    }

    private void KsUnsubscribeLayoutCVar()
    {
        if (_ksLayoutCVarHandler == null)
            return;

        _configurationManager.UnsubValueChanged(KsCCVars.LobbyLayout, _ksLayoutCVarHandler);
        _ksLayoutCVarHandler = null;
    }
}
