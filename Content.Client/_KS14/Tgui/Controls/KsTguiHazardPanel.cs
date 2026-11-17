using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     A panel behind something dangerous, under the tgui theme drawn in tgui's yellow and black hazard stripes
///         (<see cref="KsTguiHazardStyleBox"/>) with its contents 6px inside the border, as tg's anomalous weather tower has
///         round its summon button. Other themes give it nothing, so there it lays its contents out as if it weren't there.
/// </summary>
/// <remarks>
///     <para>
///         Not a <see cref="PanelContainer"/>: Baron gives every bare one its dark panel, which would frame what this holds.
///             The box comes from a style property only the tgui sheet sets, and is laid out as a panel's is.
///     </para>
///     <para>
///         tg uses the stripes in exactly one window, so keep this for the one control a window has that is meant to look
///             alarming, not for ordinary warnings, which are a <see cref="KsTguiNoticeBox"/>.
///     </para>
/// </remarks>
public sealed class KsTguiHazardPanel : Container
{
    /// <summary>
    ///     The stripes' style box, set by the tgui sheet alone, so its presence says they are drawn.
    /// </summary>
    public const string StylePropertyHazard = "ks-tgui-hazard";

    /// <summary>
    ///     tgui's <c>m={1}</c> on what the panel holds: half of its 12px <c>em</c>.
    /// </summary>
    public const float Padding = 6f;

    private bool _shown;

    /// <summary>
    ///     Whether the current theme draws the stripes.
    /// </summary>
    public bool Shown => _shown;

    /// <summary>
    ///     Raised when the theme starts or stops drawing the stripes, for a window that colours what the panel holds
    ///         differently under each.
    /// </summary>
    public event Action<bool>? ShownChanged;

    /// <summary>
    ///     What the theme draws, or null.
    /// </summary>
    public StyleBox? Box => TryGetStyleProperty<StyleBox>(StylePropertyHazard, out var box) ? box : null;

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        var shown = Box != null;
        if (shown == _shown)
            return;

        _shown = shown;
        InvalidateMeasure();
        ShownChanged?.Invoke(shown);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        Box?.Draw(handle, PixelSizeBox, UIScale);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var boxSize = Box?.MinimumSize ?? Vector2.Zero;
        var measureSize = Vector2.Max(availableSize - boxSize, Vector2.Zero);
        var childSize = Vector2.Zero;
        foreach (var child in Children)
        {
            child.Measure(measureSize);
            childSize = Vector2.Max(childSize, child.DesiredSize);
        }

        return boxSize + childSize;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var ourBox = UIBox2.FromDimensions(Vector2.Zero, finalSize);
        var contentBox = Box?.GetContentBox(ourBox, 1f) ?? ourBox;

        foreach (var child in Children)
        {
            child.Arrange(contentBox);
        }

        return finalSize;
    }
}
