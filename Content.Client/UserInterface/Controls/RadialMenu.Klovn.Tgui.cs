namespace Content.Client.UserInterface.Controls;

// Partial so a theme can colour the sectors: the colours are drawn in code, which a sheet can't reach.
public partial class RadialMenuButtonWithSector
{
    public const string KsStylePropertyBackground = "ks-radial-background";
    public const string KsStylePropertyHoverBackground = "ks-radial-hover-background";
    public const string KsStylePropertyBorder = "ks-radial-border";
    public const string KsStylePropertyHoverBorder = "ks-radial-hover-border";
    public const string KsStylePropertySeparator = "ks-radial-separator";

    // A colour somebody set in code (a door remote's, the changeling's selected identity) stays theirs.
    private bool _ksBackgroundSet;
    private bool _ksHoverBackgroundSet;
    private bool _ksBorderSet;
    private bool _ksHoverBorderSet;

    private Color KsBackgroundSrgb => KsStyled(_ksBackgroundSet, KsStylePropertyBackground, _backgroundColorSrgb);

    private Color KsHoverBackgroundSrgb => KsStyled(_ksHoverBackgroundSet, KsStylePropertyHoverBackground, _hoverBackgroundColorSrgb);

    private Color KsBorderSrgb => KsStyled(_ksBorderSet, KsStylePropertyBorder, _borderColorSrgb);

    private Color KsHoverBorderSrgb => KsStyled(_ksHoverBorderSet, KsStylePropertyHoverBorder, _hoverBorderColorSrgb);

    private Color KsSeparatorColor => TryGetStyleProperty<Color>(KsStylePropertySeparator, out var separator) ? separator : SeparatorColor;

    /// <summary>
    ///     The colour a sector draws with, in sRGB as the primitives want it. Style properties hold ordinary colours.
    /// </summary>
    public Color KsResolveBackground(bool hovered)
    {
        return Color.FromSrgb(hovered ? KsHoverBackgroundSrgb : KsBackgroundSrgb);
    }

    private Color KsStyled(bool explicitlySet, string property, Color fallbackSrgb)
    {
        if (!explicitlySet && TryGetStyleProperty<Color>(property, out var styled))
            return Color.ToSrgb(styled);

        return fallbackSrgb;
    }
}
