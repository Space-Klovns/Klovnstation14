using Content.Client.Stylesheets.Palette;

namespace Content.Client.Stylesheets.Stylesheets;

public partial class NanotrasenStylesheet
{
    public override ColorPalette PrimaryPalette => _ksTgui ? Content.Client._KS14.Tgui.KsTguiPalettes.Primary : Palettes.Navy; // KS14: Palettes.Navy -> follows the theme
    public override ColorPalette SecondaryPalette => _ksTgui ? Content.Client._KS14.Tgui.KsTguiPalettes.Secondary : Palettes.Slate; // KS14: Palettes.Slate -> follows the theme
    public override ColorPalette PositivePalette => _ksTgui ? Content.Client._KS14.Tgui.KsTguiPalettes.Positive : Palettes.Green; // KS14: Palettes.Green -> follows the theme
    public override ColorPalette NegativePalette => _ksTgui ? Content.Client._KS14.Tgui.KsTguiPalettes.Negative : Palettes.Red; // KS14: Palettes.Red -> follows the theme
    public override ColorPalette HighlightPalette => _ksTgui ? Content.Client._KS14.Tgui.KsTguiPalettes.Highlight : Palettes.Gold; // KS14: Palettes.Gold -> follows the theme
}
