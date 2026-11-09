using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     A panel that is a tgui Section body under the tgui theme and the flat <see cref="BaronColor"/> under Baron. For
///         retrofitting windows: replace a <c>PanelContainer</c> whose <c>PanelOverride</c> is a flat colour, and give that
///         colour as <see cref="BaronColor"/>.
/// </summary>
/// <remarks>
///     Which one is decided by a style property only the tgui sheet sets, so a live toggle needs no cvar read. A panel
///         override beats the sheet, so under tgui it has to be null for the rule's box to show.
/// </remarks>
[Virtual]
public class KsSectionPanel : PanelContainer
{
    public const string StyleClassSectionPanel = "KsSectionPanel";

    private Color? _baronColor;

    public KsSectionPanel()
    {
        AddStyleClass(StyleClassSectionPanel);
        ApplyPanel(tgui: false);
    }

    /// <summary>
    ///     The flat colour under Baron. Null is see-through.
    /// </summary>
    public Color? BaronColor
    {
        get => _baronColor;
        set
        {
            _baronColor = value;
            ApplyPanel(IsTgui());
        }
    }

    private static readonly StyleBox Empty = new StyleBoxEmpty();

    /// <summary>
    ///     True for a section inside another, which tgui leaves see-through so the outer body shows.
    /// </summary>
    protected bool Transparent
    {
        get => _transparent;
        set
        {
            _transparent = value;
            ApplyPanel(IsTgui());
        }
    }

    private bool _transparent;

    private bool IsTgui()
    {
        return TryGetStyleProperty<bool>(KsTguiSheetlet.StylePropertySection, out var enabled) && enabled;
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();
        ApplyPanel(IsTgui());
    }

    private void ApplyPanel(bool tgui)
    {
        if (tgui && !_transparent)
            PanelOverride = null;
        else if (tgui)
            PanelOverride = Empty;
        else
            PanelOverride = _baronColor is { } color ? new StyleBoxFlat(color) : Empty;
    }
}
