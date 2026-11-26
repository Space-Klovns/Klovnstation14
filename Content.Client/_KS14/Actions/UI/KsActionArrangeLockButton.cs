using System.Numerics;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.TextureRect;

namespace Content.Client._KS14.Actions.UI;

/// <summary>
///     The small padlock beside the top of the action bar. Closed, nothing on the bar or loose on the screen can be
///         dragged, docked or popped out, so a hurried click only ever uses an action. Open, actions can be dragged
///         around, off the bar, and onto the bin. Click it to switch.
/// </summary>
public sealed class KsActionArrangeLockButton : BaseButton
{
    private static readonly ResPath LockedIconPath = new("/Textures/Interface/VerbIcons/lock.svg.192dpi.png");
    private static readonly ResPath UnlockedIconPath = new("/Textures/Interface/VerbIcons/unlock.svg.192dpi.png");

    private static readonly Color LockedColor = Color.FromHex("#9aa4b0");
    private static readonly Color UnlockedColor = Color.FromHex("#e8c060");

    private readonly Texture _lockedIcon;
    private readonly Texture _unlockedIcon;
    private readonly StyleBoxFlat _box;
    private readonly TextureRect _icon;

    /// <summary>
    ///     Kept and rewritten rather than built per hover, so a tooltip already showing changes with the click.
    /// </summary>
    private readonly Tooltip _tooltip = new();
    private bool _locked;
    private bool _hovered;

    public KsActionArrangeLockButton()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        _lockedIcon = cache.GetTexture(LockedIconPath);
        _unlockedIcon = cache.GetTexture(UnlockedIconPath);

        SetSize = new Vector2(24, 24);
        _box = new StyleBoxFlat { BorderThickness = new Thickness(1) };
        AddChild(new PanelContainer { PanelOverride = _box, MouseFilter = MouseFilterMode.Ignore });

        _icon = new TextureRect
        {
            Stretch = StretchMode.KeepAspectCentered,
            Margin = new Thickness(4),
            MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(_icon);
        TooltipSupplier = _ => _tooltip;
        SetLocked(false);
    }

    /// <summary>
    ///     Show the padlock closed or open.
    /// </summary>
    public void SetLocked(bool locked)
    {
        _locked = locked;
        _icon.Texture = locked ? _lockedIcon : _unlockedIcon;
        _tooltip.Text = Loc.GetString(locked ? "ks-action-arrange-locked-tooltip" : "ks-action-arrange-unlocked-tooltip");
        UpdateLook();
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        _hovered = true;
        UpdateLook();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hovered = false;
        UpdateLook();
    }

    private void UpdateLook()
    {
        // Open is the state that changes what a drag does, so it gets the colour; closed sits back in grey.
        var color = _locked ? LockedColor : UnlockedColor;
        _icon.Modulate = _hovered ? Color.White : color;
        _box.BackgroundColor = Color.FromHex("#1b1b1e").WithAlpha(_hovered ? 0.85f : 0.6f);
        _box.BorderColor = color.WithAlpha(_hovered ? 0.9f : 0.45f);
    }
}
