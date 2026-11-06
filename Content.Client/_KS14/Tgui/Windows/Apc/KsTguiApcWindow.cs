using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.Shared.APC;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.Apc;

/// <summary>
///     The APC window laid out after tgui's Apc.jsx: Power Status with the main breaker, the cell and the load. KS14's APC has
///         no power channels, charge mode, cover lock, lighting switches or malfunction state, so those parts of tg's window
///         are not here.
/// </summary>
public sealed class KsTguiApcWindow : FancyWindow, IKsApcWindow
{
    private readonly KsTguiNoticeBox _lockNotice;
    private readonly KsTguiLabeledListItem _breakerItem;
    private readonly KsTguiButton _breakerButton;
    private readonly KsTguiProgressBar _chargeBar;
    private readonly KsTguiLabeledListItem _loadItem;

    private bool _hasAccess;
    private bool _breakerOn;

    public event Action? OnBreaker;

    public KsTguiApcWindow()
    {
        Title = Loc.GetString("apc-menu-title");
        SetWidth = 450f;
        MinSize = new Vector2(450f, 0f);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
        };

        _lockNotice = new KsTguiNoticeBox
        {
            HorizontalExpand = true,
            Text = Loc.GetString("apc-component-insufficient-access"),
            Visible = false,
        };
        root.AddChild(_lockNotice);

        var section = new KsTguiSection { Title = Loc.GetString("ks-tgui-apc-power-status") };
        var list = new KsTguiLabeledList();

        _breakerItem = list.AddItem(Loc.GetString("apc-menu-breaker-label"));
        _breakerButton = new KsTguiButton();
        _breakerButton.OnPressed += _ => OnBreaker?.Invoke();
        _breakerItem.Buttons = _breakerButton;

        var cellItem = list.AddItem(Loc.GetString("ks-tgui-apc-power-cell"));
        _chargeBar = new KsTguiProgressBar { Color = KsTguiColor.Good, HorizontalExpand = true };
        cellItem.XamlChildren.Add(_chargeBar);

        _loadItem = list.AddItem(Loc.GetString("ks-tgui-apc-total-load"));

        section.AddChild(list);
        root.AddChild(section);
        ContentsContainer.AddChild(root);

        RefreshBreaker();
    }

    public void SetEntity(EntityUid entity)
    {
        // tg's window has no picture of the machine.
    }

    public void SetAccessEnabled(bool hasAccess)
    {
        _hasAccess = hasAccess;
        _lockNotice.Visible = !hasAccess;
        RefreshBreaker();
    }

    public void UpdateState(BoundUserInterfaceState state)
    {
        var apcState = (ApcBoundInterfaceState)state;

        _breakerOn = apcState.MainBreaker;
        RefreshBreaker();

        var (externalKey, externalColor) = apcState.ApcExternalPower switch
        {
            ApcExternalPowerState.Good => ("ks-tgui-apc-external-good", KsTguiPalettes.Good),
            ApcExternalPowerState.Low => ("ks-tgui-apc-external-low", KsTguiPalettes.Average),
            _ => ("ks-tgui-apc-external-none", KsTguiPalettes.Bad),
        };
        _breakerItem.Text = Loc.GetString(externalKey);
        _breakerItem.TextColor = externalColor;

        _chargeBar.Value = apcState.Charge;
        _chargeBar.Text = Loc.GetString("apc-menu-charge-label", ("percent", apcState.Charge.ToString("P0")));

        _loadItem.Text = Loc.GetString("apc-menu-power-state-label-text", ("power", apcState.Power));
    }

    private void RefreshBreaker()
    {
        _breakerButton.Icon = _breakerOn ? KsTguiIcons.Power : KsTguiIcons.Times;
        _breakerButton.Text = Loc.GetString(_breakerOn ? "ks-tgui-apc-on" : "ks-tgui-apc-off");
        _breakerButton.Selected = _breakerOn && _hasAccess;
        _breakerButton.Disabled = !_hasAccess;
        _breakerButton.ToolTip = _hasAccess ? null : Loc.GetString("apc-component-insufficient-access");
    }
}
