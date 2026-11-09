using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.GameTicking.Managers;
using Content.Client.Message;
using Content.Client.UserInterface.Controls;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Windows.Pda;

/// <summary>
///     The PDA laid out after tgui's NtOS: a header with the shift time, then Details (the ID login, the station and its
///         alert level, with the flashlight, pen and ID ejects), a pAI section, the running program, the programs and the
///         PDA's settings. A program takes the whole body, as in NtosWindow. Programs themselves are drawn as they are.
///         SS14's PDA reports no battery or network state, so the header leaves those out.
/// </summary>
public sealed partial class KsTguiPdaWindow : FancyWindow, IKsPdaMenu
{
    [Dependency] private IClipboardManager _clipboardManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IEntitySystemManager _entitySystemManager = default!;

    private readonly ClientGameTicker _gameTicker;

    private readonly Label _timeLabel;
    private readonly Label _addressLabel;
    private readonly KsTguiButton _minimizeButton;
    private readonly KsTguiButton _closeProgramButton;
    private readonly KsTguiButton _powerButton;

    private readonly KsTguiScrollContainer _homeView;
    private readonly BoxContainer _programView;

    private readonly KsTguiButton _flashlightButton;
    private readonly KsTguiItemSlotButton _ejectPenButton;
    private readonly KsTguiItemSlotButton _ejectIdButton;
    private readonly KsTguiLabeledListItem _ownerRow;
    private readonly KsTguiLabeledListItem _idNameRow;
    private readonly KsTguiLabeledListItem _assignmentRow;
    private readonly KsTguiLabeledListItem _stationRow;
    private readonly KsTguiLabeledListItem _alertRow;
    private readonly KsTguiLabeledListItem _timeRow;
    private readonly RichTextLabel _instructionsLabel;

    private readonly KsTguiSection _paiSection;
    private readonly KsTguiItemSlotButton _ejectPaiButton;

    private readonly KsTguiSection _runningSection;
    private readonly KsTguiButton _resumeButton;
    private readonly KsTguiButton _closeRunningButton;

    private readonly BoxContainer _programList;

    private readonly KsTguiButton _ringtoneButton;
    private readonly KsTguiButton _musicButton;
    private readonly KsTguiButton _showUplinkButton;
    private readonly KsTguiButton _lockUplinkButton;

    private readonly List<Action> _closeHandlers = new();

    private string _pdaOwner = Loc.GetString("comp-pda-ui-unknown");
    private string _idOwner = Loc.GetString("comp-pda-ui-unknown");
    private string _jobTitle = Loc.GetString("comp-pda-ui-unassigned");
    private string _stationName = Loc.GetString("comp-pda-ui-unknown");
    private string _alertLevel = Loc.GetString("comp-pda-ui-unknown");
    private string _instructions = Loc.GetString("comp-pda-ui-unknown");

    private string? _programTitle;
    private bool _showingProgram;
    private int _shownSecond = -1;

    public BaseButton FlashLightToggleButton => _flashlightButton;
    public BaseButton EjectIdButton => _ejectIdButton;
    public BaseButton EjectPenButton => _ejectPenButton;
    public BaseButton EjectPaiButton => _ejectPaiButton;
    public BaseButton ActivateMusicButton => _musicButton;
    public BaseButton AccessRingtoneButton => _ringtoneButton;
    public BaseButton ShowUplinkButton => _showUplinkButton;
    public BaseButton LockUplinkButton => _lockUplinkButton;
    public Control ProgramView => _programView;

    // The PDA's colours belong to the stock frame.
    public string? BorderColor { set { } }
    public string? AccentHColor { set { } }
    public string? AccentVColor { set { } }

    public event Action<EntityUid>? OnProgramItemPressed;
    public event Action<EntityUid>? OnUninstallButtonPressed;
    public event Action<EntityUid>? OnInstallButtonPressed;

    public KsTguiPdaWindow()
    {
        IoCManager.InjectDependencies(this);
        _gameTicker = _entitySystemManager.GetEntitySystem<ClientGameTicker>();

        Title = Loc.GetString("ks-tgui-pda-title");
        MinSize = new Vector2(576f, 450f);
        SetSize = new Vector2(576f, 540f);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
        };

        // Header (NtosWindow__header).
        var header = new KsTguiSection();
        var headerRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        headerRow.AddChild(new KsTguiIcon { Glyph = KsTguiIcons.Calendar, VerticalAlignment = VAlignment.Center });
        _timeLabel = new Label { VerticalAlignment = VAlignment.Center };
        headerRow.AddChild(_timeLabel);
        headerRow.AddChild(new Label
        {
            Text = "Robust#OS",
            VerticalAlignment = VAlignment.Center,
            Modulate = Color.White.WithAlpha(0.5f),
        });
        _addressLabel = new Label { VerticalAlignment = VAlignment.Center, Modulate = Color.White.WithAlpha(0.5f) };
        headerRow.AddChild(_addressLabel);
        headerRow.AddChild(new Control { HorizontalExpand = true });

        _minimizeButton = MakeHeaderButton(KsTguiIcons.WindowMinimize, "ks-tgui-pda-minimize");
        _minimizeButton.OnPressed += _ => ToHomeScreen();
        _closeProgramButton = MakeHeaderButton(KsTguiIcons.Times, "ks-tgui-pda-close-program");
        _closeProgramButton.OnPressed += _ => CloseProgram();
        _powerButton = MakeHeaderButton(KsTguiIcons.Power, "ks-tgui-pda-power-off");
        _powerButton.OnPressed += _ => Close();
        headerRow.AddChild(_minimizeButton);
        headerRow.AddChild(_closeProgramButton);
        headerRow.AddChild(_powerButton);
        header.AddChild(headerRow);
        root.AddChild(header);

        // Home: what NtosMain shows.
        var homeBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            HorizontalExpand = true,
        };
        // NtosMain's NtosWindow.Content is scrollable: overflow-y: scroll.
        _homeView = new KsTguiScrollContainer
        {
            HScrollEnabled = false,
            ReserveVertical = true,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        _homeView.AddChild(homeBox);

        _runningSection = new KsTguiSection { Title = Loc.GetString("ks-tgui-pda-running"), Visible = false };
        var runningRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        _resumeButton = new KsTguiButton { Fluid = true, Color = KsTguiColor.Transparent, Icon = KsTguiIcons.Play };
        _resumeButton.OnPressed += _ => ShowProgram();
        _closeRunningButton = new KsTguiButton
        {
            Color = KsTguiColor.Transparent,
            Icon = KsTguiIcons.Times,
            ToolTip = Loc.GetString("ks-tgui-pda-close-program"),
        };
        _closeRunningButton.OnPressed += _ => CloseProgram();
        runningRow.AddChild(_resumeButton);
        runningRow.AddChild(_closeRunningButton);
        _runningSection.AddChild(runningRow);
        homeBox.AddChild(_runningSection);

        // Details: the ID login and the station, with the tools beside the title.
        var detailsButtons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        _flashlightButton = new KsTguiButton
        {
            Icon = KsTguiIcons.Lightbulb,
            ToggleMode = true,
            ToolTip = Loc.GetString("comp-pda-ui-toggle-flashlight-button"),
        };
        _ejectPenButton = new KsTguiItemSlotButton
        {
            FilledText = Loc.GetString("comp-pda-ui-eject-pen-button"),
            EmptyText = Loc.GetString("ks-tgui-pda-insert-pen"),
        };
        _ejectIdButton = new KsTguiItemSlotButton
        {
            FilledText = Loc.GetString("comp-pda-ui-eject-id-button"),
            EmptyText = Loc.GetString("ks-tgui-pda-insert-id"),
        };
        detailsButtons.AddChild(_flashlightButton);
        detailsButtons.AddChild(_ejectPenButton);
        detailsButtons.AddChild(_ejectIdButton);

        var details = new KsTguiSection { Title = Loc.GetString("ks-tgui-pda-details"), Buttons = detailsButtons };
        var detailsBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var list = new KsTguiLabeledList();
        _ownerRow = AddRow(list, "ks-tgui-pda-owner", () => _pdaOwner);
        _idNameRow = AddRow(list, "ks-tgui-pda-id-name", () => _idOwner + ", " + _jobTitle);
        _assignmentRow = AddRow(list, "ks-tgui-pda-assignment", () => _jobTitle);
        _stationRow = AddRow(list, "ks-tgui-pda-station", () => _stationName);
        _alertRow = AddRow(list, "ks-tgui-pda-alert-level", () => _alertLevel);
        _timeRow = AddRow(list, "ks-tgui-pda-shift-time", ShiftTimeText);
        detailsBox.AddChild(list);

        _instructionsLabel = new RichTextLabel { HorizontalExpand = true, Margin = new Thickness(0f, 4f, 0f, 0f) };
        var instructionsButton = new ContainerButton
        {
            HorizontalExpand = true,
            ToolTip = Loc.GetString("ks-tgui-pda-copy"),
        };
        instructionsButton.AddChild(_instructionsLabel);
        instructionsButton.OnPressed += _ => _clipboardManager.SetText(_instructions);
        detailsBox.AddChild(instructionsButton);
        details.AddChild(detailsBox);
        homeBox.AddChild(details);

        _paiSection = new KsTguiSection { Title = Loc.GetString("ks-tgui-pda-pai") };
        _ejectPaiButton = new KsTguiItemSlotButton
        {
            Fluid = true,
            Color = KsTguiColor.Transparent,
            FilledText = Loc.GetString("ks-tgui-pda-eject-pai"),
            EmptyText = Loc.GetString("ks-tgui-pda-insert-pai"),
            LeftAligned = true,
        };
        _ejectPaiButton.Label.FontColorOverride = Color.White;
        _paiSection.AddChild(_ejectPaiButton);
        homeBox.AddChild(_paiSection);

        var programsSection = new KsTguiSection { Title = Loc.GetString("comp-pda-io-program-list-button") };
        _programList = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        programsSection.AddChild(_programList);
        homeBox.AddChild(programsSection);

        var settingsSection = new KsTguiSection { Title = Loc.GetString("comp-pda-io-settings-button") };
        var settingsBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _ringtoneButton = MakeSettingsButton(settingsBox, KsTguiIcons.Bell,
            "comp-pda-ui-ringtone-button", "comp-pda-ui-ringtone-button-description");
        _musicButton = MakeSettingsButton(settingsBox, KsTguiIcons.Music,
            "pda-bound-user-interface-music-button", "pda-bound-user-interface-music-button-description");
        _showUplinkButton = MakeSettingsButton(settingsBox, KsTguiIcons.Unlock,
            "pda-bound-user-interface-show-uplink-title", "pda-bound-user-interface-show-uplink-description");
        _lockUplinkButton = MakeSettingsButton(settingsBox, KsTguiIcons.Lock,
            "pda-bound-user-interface-lock-uplink-title", "pda-bound-user-interface-lock-uplink-description");
        _musicButton.Visible = false;
        _showUplinkButton.Visible = false;
        _lockUplinkButton.Visible = false;
        settingsSection.AddChild(settingsBox);
        homeBox.AddChild(settingsSection);

        // A program has the body to itself.
        _programView = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false,
        };

        var body = new Control { HorizontalExpand = true, VerticalExpand = true };
        body.AddChild(_homeView);
        body.AddChild(_programView);
        root.AddChild(body);

        ContentsContainer.AddChild(root);

        UpdateHeader();
    }

    private static KsTguiButton MakeHeaderButton(string icon, string tooltipKey)
    {
        return new KsTguiButton
        {
            Color = KsTguiColor.Transparent,
            Icon = icon,
            ToolTip = Loc.GetString(tooltipKey),
        };
    }

    private static KsTguiButton MakeSettingsButton(BoxContainer parent, string icon, string textKey, string descriptionKey)
    {
        var button = new KsTguiButton
        {
            Fluid = true,
            Color = KsTguiColor.Transparent,
            Icon = icon,
            Text = Loc.GetString(textKey),
            ToolTip = Loc.GetString(descriptionKey),
            LeftAligned = true,
        };
        button.Label.FontColorOverride = Color.White;
        parent.AddChild(button);
        return button;
    }

    /// <summary>
    ///     A row of Details whose value can be copied, as the stock PDA's lines can.
    /// </summary>
    private KsTguiLabeledListItem AddRow(KsTguiLabeledList list, string labelKey, Func<string> copyText)
    {
        var row = list.AddItem(Loc.GetString(labelKey));
        var copyButton = new KsTguiButton
        {
            Compact = true,
            Color = KsTguiColor.Transparent,
            Icon = KsTguiIcons.Copy,
            ToolTip = Loc.GetString("ks-tgui-pda-copy"),
        };
        copyButton.OnPressed += _ => _clipboardManager.SetText(copyText());
        row.Buttons = copyButton;
        return row;
    }

    private string ShiftTimeText()
    {
        return _gameTiming.CurTime.Subtract(_gameTicker.RoundStartTimeSpan).ToString("hh\\:mm\\:ss");
    }

    public void AddProgramCloseHandler(Action handler)
    {
        _closeHandlers.Add(handler);
    }

    private void CloseProgram()
    {
        HideProgramHeader();
        ToHomeScreen();

        foreach (var handler in _closeHandlers)
            handler();
    }

    public void UpdateState(PdaUpdateState state)
    {
        _flashlightButton.Pressed = state.FlashlightEnabled;
        _flashlightButton.Selected = state.FlashlightEnabled;
        _flashlightButton.Color = state.FlashlightEnabled ? KsTguiColor.Default : KsTguiColor.Bad;

        if (state.PdaOwnerInfo.ActualOwnerName != null)
        {
            _pdaOwner = state.PdaOwnerInfo.ActualOwnerName;
            _ownerRow.Text = _pdaOwner;
            _ownerRow.Visible = true;
        }
        else
        {
            _ownerRow.Visible = false;
        }

        var hasId = state.PdaOwnerInfo.IdOwner != null || state.PdaOwnerInfo.JobTitle != null;
        if (hasId)
        {
            _idOwner = state.PdaOwnerInfo.IdOwner ?? Loc.GetString("comp-pda-ui-unknown");
            _jobTitle = state.PdaOwnerInfo.JobTitle ?? Loc.GetString("comp-pda-ui-unassigned");
            _idNameRow.Text = _idOwner;
            _assignmentRow.Text = _jobTitle;
        }
        else
        {
            _idNameRow.Text = string.Empty;
            _assignmentRow.Text = string.Empty;
        }

        _stationName = state.StationName ?? Loc.GetString("comp-pda-ui-unknown");
        _stationRow.Text = _stationName;

        var alertLevel = state.PdaOwnerInfo.StationAlertLevel;
        var alertLevelKey = alertLevel != null ? $"alert-level-{alertLevel}" : "alert-level-unknown";
        _alertLevel = Loc.GetString(alertLevelKey);
        _alertRow.Text = _alertLevel;
        _alertRow.TextColor = state.PdaOwnerInfo.StationAlertColor;

        _instructions = Loc.GetString($"{alertLevelKey}-instructions");
        _instructionsLabel.SetMarkup(Loc.GetString("comp-pda-ui-station-alert-level-instructions",
            ("instructions", _instructions)));

        _addressLabel.Text = state.Address?.ToUpper() ?? " - ";

        _ejectIdButton.Filled = hasId;
        _ejectPenButton.Filled = state.HasPen;
        _ejectPaiButton.Filled = state.HasPai; // the pAI slot always shows, as Insert when empty
        _musicButton.Visible = state.CanPlayMusic;
        _showUplinkButton.Visible = state.HasUplink;
        _lockUplinkButton.Visible = state.HasUplink;

        UpdateTime();
    }

    public void UpdateAvailablePrograms(List<(EntityUid, CartridgeComponent)> programs)
    {
        _programList.RemoveAllChildren();

        if (programs.Count == 0)
        {
            _programList.AddChild(new Label
            {
                Text = Loc.GetString("comp-pda-io-no-programs-available"),
                HorizontalAlignment = HAlignment.Center,
            });

            return;
        }

        foreach (var (uid, component) in programs)
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };

            var icon = new AnimatedTextureRect
            {
                VerticalAlignment = VAlignment.Center,
                Margin = new Thickness(0f, 0f, 4f, 0f),
            };
            icon.DisplayRect.Stretch = TextureRect.StretchMode.KeepAspectCentered;
            icon.DisplayRect.SetSize = new Vector2(24f, 24f);
            if (component.Icon is not null)
                icon.SetFromSpriteSpecifier(component.Icon);
            row.AddChild(icon);

            var open = new KsTguiButton
            {
                Fluid = true,
                Color = KsTguiColor.Transparent,
                Text = Loc.GetString(component.ProgramName),
                LeftAligned = true,
            };
            open.Label.FontColorOverride = Color.White;
            open.OnPressed += _ => OnProgramItemPressed?.Invoke(uid);
            row.AddChild(open);

            switch (component.InstallationStatus)
            {
                case InstallationStatus.Cartridge:
                    var install = new KsTguiButton
                    {
                        Compact = true,
                        Text = Loc.GetString("cartridge-bound-user-interface-install-button"),
                    };
                    install.OnPressed += _ => OnInstallButtonPressed?.Invoke(uid);
                    row.AddChild(install);
                    break;
                case InstallationStatus.Installed:
                    var uninstall = new KsTguiButton
                    {
                        Compact = true,
                        Text = Loc.GetString("cartridge-bound-user-interface-uninstall-button"),
                    };
                    uninstall.OnPressed += _ => OnUninstallButtonPressed?.Invoke(uid);
                    row.AddChild(uninstall);
                    break;
            }

            _programList.AddChild(row);
        }
    }

    public void ToHomeScreen()
    {
        _showingProgram = false;
        _homeView.Visible = true;
        _programView.Visible = false;
        UpdateHeader();
    }

    public void HideProgramHeader()
    {
        _programTitle = null;
        UpdateHeader();
    }

    public void ToProgramView(string title)
    {
        _programTitle = title;
        ShowProgram();
    }

    private void ShowProgram()
    {
        _showingProgram = true;
        _homeView.Visible = false;
        _programView.Visible = true;
        UpdateHeader();
    }

    /// <summary>
    ///     The title bar, the header's buttons and the Running section follow what is open: tgui's header offers minimise
    ///         and close while a program has the window, and power-off on the main menu.
    /// </summary>
    private void UpdateHeader()
    {
        var inProgram = _showingProgram && _programTitle != null;

        _minimizeButton.Visible = inProgram;
        _closeProgramButton.Visible = inProgram;
        _powerButton.Visible = !inProgram;

        _runningSection.Visible = _programTitle != null && !inProgram;
        _resumeButton.Text = _programTitle ?? string.Empty;

        Title = inProgram ? _programTitle : Loc.GetString("ks-tgui-pda-title");
    }

    private void UpdateTime()
    {
        var text = ShiftTimeText();
        _timeLabel.Text = text;
        _timeRow.Text = text;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // The text only changes once a second.
        var second = (int)_gameTiming.CurTime.Subtract(_gameTicker.RoundStartTimeSpan).TotalSeconds;
        if (second == _shownSecond)
            return;

        _shownSecond = second;
        UpdateTime();
    }
}
