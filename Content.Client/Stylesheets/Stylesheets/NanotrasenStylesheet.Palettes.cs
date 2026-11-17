using Content.Client.Stylesheets.Palette;

namespace Content.Client.Stylesheets.Stylesheets;

public partial class NanotrasenStylesheet
{
    public override ColorPalette PrimaryPalette => _ksTgui ? _ksPalette.PrimaryPalette : Palettes.Navy; // KS14: Palettes.Navy -> follows the theme
    public override ColorPalette SecondaryPalette => _ksTgui ? _ksPalette.SecondaryPalette : Palettes.Slate; // KS14: Palettes.Slate -> follows the theme
    public override ColorPalette PositivePalette => _ksTgui ? _ksPalette.PositivePalette : Palettes.Green; // KS14: Palettes.Green -> follows the theme
    public override ColorPalette NegativePalette => _ksTgui ? _ksPalette.NegativePalette : Palettes.Red; // KS14: Palettes.Red -> follows the theme
    public override ColorPalette HighlightPalette => _ksTgui ? _ksPalette.HighlightPalette : Palettes.Gold; // KS14: Palettes.Gold -> follows the theme
}
