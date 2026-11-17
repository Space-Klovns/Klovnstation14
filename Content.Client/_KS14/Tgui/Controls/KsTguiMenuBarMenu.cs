using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's <c>.MenuBar__menu</c>: the column of entries a dropdown or submenu opens. Background of the bar, 4px of padding
///         (<c>--space-sm</c>), and <c>box-shadow: 4px 6px 5px -2px hsla(0, 0%, 0%, .5)</c> behind it, which here is a few
///         stacked translucent boxes. Entries are as wide as the menu: <see cref="OpenWidth"/> if one is given, otherwise the
///         widest of them. It shows in a popup, which closes on a click anywhere outside it.
/// </summary>
public sealed class KsTguiMenuBarMenu : Control
{
    /// <summary>
    ///     <c>--space-sm</c>, the menu's padding.
    /// </summary>
    public const float Padding = 4f;

    /// <summary>
    ///     How far the shadow reaches past the menu on each side: its 5px blur, 4px/6px offset and -2px spread.
    /// </summary>
    private static readonly Thickness ShadowReach = new(2f, 0f, 8f, 10f);

    private const int ShadowLayers = 5;

    private Popup? _popup;
    private bool _hovered;

    public KsTguiMenuBarMenu()
    {
        MouseFilter = MouseFilterMode.Stop;
    }

    /// <summary>
    ///     Raised when the popup is gone, whoever closed it.
    /// </summary>
    public event Action? Closed;

    /// <summary>
    ///     tgui's <c>openWidth</c> in pixels; zero fits the entries.
    /// </summary>
    public float OpenWidth { get; set; }

    public bool IsOpen => _popup is { Visible: true };

    /// <summary>
    ///     The entries, in order.
    /// </summary>
    public IEnumerable<Control> Entries => Children;

    public bool Hovered => _hovered;

    /// <summary>
    ///     Shows the menu with its top left corner here, in the root's coordinates.
    /// </summary>
    public void OpenAt(Vector2 topLeft)
    {
        if (_popup == null)
        {
            _popup = new Popup();
            _popup.AddChild(this);
            _popup.OnPopupHide += OnPopupHide;
        }

        if (_popup.Parent == null)
            UserInterfaceManager.ModalRoot.AddChild(_popup);

        _popup.Open(UIBox2.FromDimensions(topLeft - new Vector2(ShadowReach.Left, ShadowReach.Top), Vector2.Zero));
    }

    public void Close()
    {
        _popup?.Close();
    }

    private void OnPopupHide()
    {
        _hovered = false;
        _popup?.Orphan();
        Closed?.Invoke();
    }

    private float EntriesWidth()
    {
        var widest = 0f;
        foreach (var child in Children)
        {
            if (child.Visible)
                widest = MathF.Max(widest, child.DesiredSize.X);
        }

        return widest;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var height = 0f;
        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            child.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
            height += child.DesiredSize.Y;
        }

        var width = OpenWidth > 0f ? OpenWidth : EntriesWidth() + Padding * 2f;
        return new Vector2(
            width + ShadowReach.Left + ShadowReach.Right,
            height + Padding * 2f + ShadowReach.Top + ShadowReach.Bottom);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var entryWidth = finalSize.X - ShadowReach.Left - ShadowReach.Right - Padding * 2f;
        var y = ShadowReach.Top + Padding;
        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            child.Arrange(UIBox2.FromDimensions(ShadowReach.Left + Padding, y, MathF.Max(0f, entryWidth), child.DesiredSize.Y));
            y += child.DesiredSize.Y;
        }

        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var menu = new UIBox2(
            ShadowReach.Left * UIScale,
            ShadowReach.Top * UIScale,
            PixelWidth - ShadowReach.Right * UIScale,
            PixelHeight - ShadowReach.Bottom * UIScale);

        // The shadow is the menu moved 4px right and 6px down, shrunk by the 2px spread, with the blur's 5px split over the
        //      layers from outside in.
        var shadowBase = new UIBox2(
            menu.Left + (4f - 2f) * UIScale,
            menu.Top + (6f - 2f) * UIScale,
            menu.Right + (4f + 2f) * UIScale,
            menu.Bottom + (6f + 2f) * UIScale);
        var layerColor = KsTguiPalettes.Translucent(Color.Black, 0.5f / (float)ShadowLayers, Background());
        for (var layer = 0; layer < ShadowLayers; layer++)
        {
            var grow = (2f - (float)layer) * UIScale;
            var box = new UIBox2(shadowBase.Left - grow, shadowBase.Top - grow, shadowBase.Right + grow, shadowBase.Bottom + grow);
            handle.DrawRect(box, layerColor);
        }

        handle.DrawRect(menu, KsTguiMenuBar.Background);
    }

    private static Color Background()
    {
        return KsTguiPalettes.Base;
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();

        _hovered = true;
    }

    protected override void MouseExited()
    {
        base.MouseExited();

        _hovered = false;
    }
}
