using System.Numerics;
using Content.Client.Resources;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.TextureRect;

namespace Content.Client._KS14.Actions.UI;

/// <summary>
///     The bin that shows beside the top of the action bar while an action is being dragged. It looks like one more
///         slot, with a bin on it; dropping an action on it takes the action off the bar and off the screen. The
///         actions menu still has it.
/// </summary>
public sealed class KsActionTrashSlot : Control
{
    private static readonly ResPath IconPath = new("/Textures/Interface/VerbIcons/delete.svg.192dpi.png");

    private static readonly Color IconColor = Color.FromHex("#c8ccd4");
    private static readonly Color IconHoverColor = Color.FromHex("#ff5a4d");

    private readonly TextureRect _background;
    private readonly PanelContainer _highlight;
    private readonly TextureRect _icon;

    public KsActionTrashSlot()
    {
        MouseFilter = MouseFilterMode.Stop;
        SetSize = new Vector2(64, 64);
        ToolTip = Loc.GetString("ks-action-trash-tooltip");

        // The same slot art and transparency as an action button, so it reads as part of the bar.
        _background = new TextureRect
        {
            TextureScale = new Vector2(2, 2),
            Modulate = new Color(255, 255, 255, 150),
            MouseFilter = MouseFilterMode.Ignore,
        };
        _highlight = new PanelContainer
        {
            StyleClasses = { ActionButton.StyleClassActionHighlightRect },
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
        };
        _icon = new TextureRect
        {
            Texture = IoCManager.Resolve<IResourceCache>().GetTexture(IconPath),
            Stretch = StretchMode.KeepAspectCentered,
            Margin = new Thickness(14),
            Modulate = IconColor,
            MouseFilter = MouseFilterMode.Ignore,
        };

        AddChild(_background);
        AddChild(_highlight);
        AddChild(_icon);
        OnThemeUpdated();
    }

    protected override void OnThemeUpdated()
    {
        base.OnThemeUpdated();
        _background.Texture = Theme.ResolveTexture("SlotBackground");
    }

    /// <summary>
    ///     Light the bin up while a dragged action is over it, so it's clear the drop will land here.
    /// </summary>
    public void SetHovered(bool hovered)
    {
        _highlight.Visible = hovered;
        _icon.Modulate = hovered ? IconHoverColor : IconColor;
    }
}
