using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     The title row of a Section, wrapping whatever children it is given (an existing heading label and its buttons). Under
///         the tgui theme it has tgui's padding and the blue line under it; under Baron it is transparent with no margins,
///         so the window it sits in is unchanged. Decided like <see cref="KsSectionPanel"/>.
/// </summary>
public sealed class KsSectionTitle : PanelContainer
{
    public const string StyleClassSectionTitle = "KsSectionTitle";

    public KsSectionTitle()
    {
        AddStyleClass(StyleClassSectionTitle);
        ApplyPanel(tgui: false);
    }

    private static readonly StyleBox Empty = new StyleBoxEmpty();

    private bool IsTgui()
    {
        return TryGetStyleProperty<bool>(KsTguiSheetlet.StylePropertySectionTitle, out var enabled) && enabled;
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();
        ApplyPanel(IsTgui());
    }

    private void ApplyPanel(bool tgui)
    {
        PanelOverride = tgui ? null : Empty;
    }
}
