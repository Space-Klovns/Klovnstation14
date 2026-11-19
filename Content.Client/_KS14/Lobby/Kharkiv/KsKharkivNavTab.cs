using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Lobby.Kharkiv;

/// <summary>
///     One tab of the main menu's bottom bar: an icon and a label, drawn flat. It sets its own colours and box from its
///     draw mode, so it looks the same under every UI theme instead of waiting on a stylesheet rule that could lose to one.
/// </summary>
public sealed class KsKharkivNavTab : ContainerButton
{
    private static readonly Color NormalColor = Color.FromHex("#D2D2D2");
    private static readonly Color HoverColor = Color.White;
    private static readonly Color DisabledColor = Color.FromHex("#6E6E6E");
    private static readonly Color BlockFill = Color.FromHex("#161616");
    private static readonly Color BlockHoverFill = Color.FromHex("#2A2A2A");
    private static readonly Color ActiveFill = Color.FromHex("#C8C8BE");
    private static readonly Color ActiveHoverFill = Color.FromHex("#DADAD0");
    private static readonly Color ActiveTextColor = Color.FromHex("#000000");
    private static readonly Color SlotBorder = Color.FromHex("#3A3A3A");

    private readonly TextureRect _icon;
    private readonly Label _label;

    private bool _active;
    private bool _dim;
    private bool _slot;
    private bool _opaque;

    /// <summary>Height of every tab: they sit inside the bar, a few pixels clear of its edges.</summary>
    public const float TabHeight = 28f;

    /// <summary>How opaque an ordinary tab's block is: the bar and its tabs are a little see-through.</summary>
    private const float TranslucentAlpha = 0.8f;

    public KsKharkivNavTab()
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 7,
            MouseFilter = MouseFilterMode.Ignore,
        };

        _icon = new TextureRect
        {
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            MinSize = new System.Numerics.Vector2(17, 17),
            MaxSize = new System.Numerics.Vector2(17, 17),
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };

        _label = new Label
        {
            StyleClasses = { KsKharkivStyleClasses.NavLabel },
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
            Visible = false,
        };

        row.AddChild(_icon);
        row.AddChild(_label);
        AddChild(row);

        SetHeight = TabHeight;
        DrawModeChanged();
    }

    /// <summary>Text after the icon. Empty for an icon-only tab.</summary>
    public string? Text
    {
        get => _label.Text;
        set
        {
            _label.Text = value;
            _label.Visible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>Resource path of the icon texture.</summary>
    public string? IconPath
    {
        set
        {
            _icon.Texture = value == null
                ? null
                : IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>(value).Texture;
        }
    }

    /// <summary>The tab of the screen the player is on: drawn as a raised block.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            DrawModeChanged();
        }
    }

    /// <summary>
    ///     Drawn solid rather than a little see-through, as the persistent tabs are. The active tab and slots always are.
    /// </summary>
    public bool Opaque
    {
        get => _opaque;
        set
        {
            _opaque = value;
            DrawModeChanged();
        }
    }

    /// <summary>A square, outlined cell for one squad slot rather than a tab.</summary>
    public bool Slot
    {
        get => _slot;
        set
        {
            _slot = value;
            DrawModeChanged();
        }
    }

    /// <summary>An empty slot: drawn faint.</summary>
    public bool Dim
    {
        get => _dim;
        set
        {
            _dim = value;
            DrawModeChanged();
        }
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();

        // The base constructor runs this before ours has made the children.
        if (_label is null || _icon is null)
            return;

        var hovered = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed;
        Color color;
        if (_active)
            color = ActiveTextColor;
        else if (DrawMode == DrawModeEnum.Disabled)
            color = DisabledColor;
        else
            color = hovered ? HoverColor : NormalColor;

        if (_dim)
            color = color.WithAlpha(hovered ? 0.7f : 0.38f);

        _label.FontColorOverride = color;
        _icon.ModulateSelfOverride = color;

        // The active tab is a light block with dark text; every other tab sits on a dark block a shade off the bar.
        var fill = _active
            ? (hovered ? ActiveHoverFill : ActiveFill)
            : (hovered ? BlockHoverFill : BlockFill);
        if (!_active && !_slot && !_opaque)
            fill = fill.WithAlpha(TranslucentAlpha);

        var box = new StyleBoxFlat(fill);
        if (_slot)
        {
            box.BorderColor = SlotBorder;
            box.BorderThickness = new Thickness(1);
        }

        var horizontal = _slot ? 6f : 10f;
        box.ContentMarginLeftOverride = horizontal;
        box.ContentMarginRightOverride = horizontal;
        box.ContentMarginTopOverride = 0;
        box.ContentMarginBottomOverride = 0;
        StyleBoxOverride = box;
    }
}
