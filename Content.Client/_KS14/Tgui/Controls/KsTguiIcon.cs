using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     A Font Awesome glyph, as tgui's Icon. The font is loaded from the bundled file whatever the theme, so it also
///         shows under Baron.
/// </summary>
public sealed partial class KsTguiIcon : Label
{
    private const float BaseSize = 12f;

    [Dependency] private KsTguiFonts _fonts = default!;

    private string? _glyph;
    private float _scale = 1f;

    public KsTguiIcon()
    {
        IoCManager.InjectDependencies(this);
        RefreshFont();
    }

    /// <summary>
    ///     The glyph to show, normally a <see cref="KsTguiIcons"/> constant.
    /// </summary>
    public string? Glyph
    {
        get => _glyph;
        set
        {
            _glyph = value;
            Text = value;
        }
    }

    /// <summary>
    ///     Size in ems, as tgui's <c>size</c>: 1 is 12 pixels.
    /// </summary>
    public float IconSize
    {
        get => _scale;
        set
        {
            _scale = value;
            RefreshFont();
        }
    }

    /// <summary>
    ///     The glyph's colour; null follows the surrounding text.
    /// </summary>
    public Color? IconColor
    {
        get => FontColorOverride;
        set => FontColorOverride = value;
    }

    private void RefreshFont()
    {
        FontOverride = _fonts.GetIconFont(Math.Max(1, (int)MathF.Round(BaseSize * _scale)));
    }
}
