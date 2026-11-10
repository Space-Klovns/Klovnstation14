using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.Shared.Disposal.Components;
using Content.Shared.Disposal.Unit;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Windows.Disposal;

/// <summary>
///     The disposal unit laid out after tgui's DisposalUnit: State, Pressure, Handle, Eject and Power in one list. The mailing
///         unit uses the same window with a destination section above it, as tg has no mailing unit.
/// </summary>
[Robust.Shared.Analyzers.Virtual]
public partial class KsTguiDisposalWindow : FancyWindow
{
    [Dependency] private IGameTiming _gameTiming = default!;

    private readonly KsTguiLabeledListItem _stateItem;
    private readonly KsTguiProgressBar _pressureBar;
    private readonly KsTguiButton _engageButton;
    private readonly KsTguiButton _ejectButton;
    private readonly KsTguiButton _powerButton;

    private readonly KsTguiSection? _destinationSection;
    private readonly BoxContainer? _targetBox;
    private readonly bool _mailing;

    private TimeSpan _fullPressure;

    public event Action? OnEject;
    public event Action? OnEngage;
    public event Action? OnPower;
    public event Action<string>? OnTargetSelected;

    public KsTguiDisposalWindow() : this(mailing: false)
    {
    }

    protected KsTguiDisposalWindow(bool mailing)
    {
        IoCManager.InjectDependencies(this);

        _mailing = mailing;
        Title = Loc.GetString("ui-disposal-unit-title");
        MinSize = new Vector2(300f, 0f);
        if (mailing)
            SetSize = new Vector2(300f, 400f);
        else
            SetWidth = 300f;

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
        };

        if (mailing)
        {
            _destinationSection = new KsTguiSection
            {
                Title = Loc.GetString("ui-mailing-unit-target-label"),
                Fill = true,
                Scrollable = true,
            };
            _targetBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 2,
            };
            _destinationSection.AddChild(_targetBox);
            root.AddChild(_destinationSection);
        }

        var section = new KsTguiSection();
        var list = new KsTguiLabeledList();

        _stateItem = list.AddItem(Loc.GetString("ui-disposal-unit-label-state").TrimEnd(':', ' '));

        var pressureItem = list.AddItem(Loc.GetString("ui-disposal-unit-label-pressure").TrimEnd(':', ' '));
        _pressureBar = new KsTguiProgressBar { Color = KsTguiColor.Good, HorizontalExpand = true };
        pressureItem.XamlChildren.Add(_pressureBar);

        var handleItem = list.AddItem(Loc.GetString("ks-tgui-disposal-handle"));
        _engageButton = new KsTguiButton
        {
            Text = Loc.GetString(mailing ? "ui-mailing-unit-button-flush" : "ui-disposal-unit-button-flush"),
        };
        _engageButton.OnPressed += _ => OnEngage?.Invoke();
        handleItem.XamlChildren.Add(_engageButton);

        var ejectItem = list.AddItem(Loc.GetString("ks-tgui-disposal-eject"));
        _ejectButton = new KsTguiButton
        {
            Icon = KsTguiIcons.SignOut,
            Text = Loc.GetString("ui-disposal-unit-button-eject"),
        };
        _ejectButton.OnPressed += _ => OnEject?.Invoke();
        ejectItem.XamlChildren.Add(_ejectButton);

        var powerItem = list.AddItem(Loc.GetString("ui-disposal-unit-button-power"));
        _powerButton = new KsTguiButton { Icon = KsTguiIcons.Power };
        _powerButton.OnPressed += _ => OnPower?.Invoke();
        powerItem.XamlChildren.Add(_powerButton);

        section.AddChild(list);
        root.AddChild(section);
        ContentsContainer.AddChild(root);

        SetEngaged(false);
    }

    /// <summary>
    ///     Shows the unit's state. <paramref name="state"/> picks the colour tg gives it, and an unpowered unit that is not
    ///         ready reads as off.
    /// </summary>
    public void SetState(DisposalsPressureState state, bool powered, bool engaged, TimeSpan fullPressure)
    {
        if (state == DisposalsPressureState.Ready)
        {
            _stateItem.Text = Loc.GetString($"disposal-unit-state-{state}");
            _stateItem.TextColor = KsTguiPalettes.Good;
        }
        else if (!powered)
        {
            _stateItem.Text = Loc.GetString("ks-tgui-disposal-off");
            _stateItem.TextColor = KsTguiPalettes.Bad;
        }
        else
        {
            _stateItem.Text = Loc.GetString($"disposal-unit-state-{state}");
            _stateItem.TextColor = KsTguiPalettes.Average;
        }

        _powerButton.Selected = powered;
        _fullPressure = fullPressure;
        SetEngaged(engaged);
        UpdatePressure();
    }

    /// <summary>
    ///     Mailing unit only: the destination list, with <paramref name="target"/> selected.
    /// </summary>
    public void SetTargets(string tag, string? target, IReadOnlyList<string> targets)
    {
        if (!_mailing || _targetBox == null || _destinationSection == null)
            return;

        Title = Loc.GetString("ui-mailing-unit-window-title", ("tag", tag));
        _destinationSection.Title = $"{Loc.GetString("ui-mailing-unit-target-label")} {target}";

        _targetBox.RemoveAllChildren();
        foreach (var name in targets)
        {
            var button = new KsTguiButton
            {
                Text = name,
                Fluid = true,
                LeftAligned = true,
                Selected = name == target,
            };
            button.OnPressed += _ => OnTargetSelected?.Invoke(name);
            _targetBox.AddChild(button);
        }
    }

    private void SetEngaged(bool engaged)
    {
        _engageButton.Icon = engaged ? KsTguiIcons.ToggleOn : KsTguiIcons.ToggleOff;
        _engageButton.Selected = engaged;
    }

    private void UpdatePressure()
    {
        var pressure = (float)Math.Min(1.0,
            1.0 - (_fullPressure.TotalSeconds - _gameTiming.CurTime.TotalSeconds) * SharedDisposalUnitSystem.PressurePerSecond);
        _pressureBar.Value = Math.Clamp(pressure, 0f, 1f);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdatePressure();
    }
}

/// <summary>
///     <see cref="KsTguiDisposalWindow"/> with the mailing unit's destination list.
/// </summary>
public sealed partial class KsTguiMailingWindow : KsTguiDisposalWindow
{
    public KsTguiMailingWindow() : base(mailing: true)
    {
    }
}
