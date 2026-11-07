using System.Linq;
using Content.Client.Resources;
using Content.Client.Stylesheets.Fonts;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The tgui font: the player's own Verdana where they have one, which is what tgui uses, and bundled DejaVu Sans where
///         they don't. Verdana can't be shipped; DejaVu shares its design origins and covers Cyrillic.
/// </summary>
public sealed partial class KsTguiFonts
{
    private const string BundledRoot = "/Fonts/_KS14/Tgui/";
    private const string IconFontFileName = "fa-solid-900.ttf";
    private const string RegularIconFontFileName = "fa-regular-400.ttf"; // KS14: Font Awesome Regular, for tgui's "-o" icons

    /// <summary>
    ///     A font with no glyphs and no height. See <see cref="GetFont"/>.
    /// </summary>
    private const string SizeCarrierFileName = "size_carrier.ttf";

    private static readonly string[] FallbackPaths =
    [
        "/Fonts/NotoSans/NotoSans-Regular.ttf",
        "/Fonts/NotoSans/NotoSansSymbols-Regular.ttf",
        "/Fonts/NotoSans/NotoSansSymbols2-Regular.ttf",
    ];

    private static readonly FontKind[] Kinds = [FontKind.Regular, FontKind.Bold, FontKind.Italic, FontKind.BoldItalic];

    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private ISystemFontManager _systemFontManager = default!;

    private readonly Dictionary<(FontKind Kind, int Size), Font> _fonts = new();
    private readonly Dictionary<(FontKind Kind, int Size), Font> _monoFonts = new();
    private readonly Dictionary<int, Font> _iconFonts = new();
    private Dictionary<FontKind, ISystemFontFace>? _systemMonoFaces;
    private Dictionary<FontKind, ISystemFontFace>? _systemFaces;

    /// <summary>
    ///     A font of this size in pixels and kind, with the Noto stack behind it for glyphs the primary lacks.
    /// </summary>
    /// <remarks>
    ///     The engine's font sizes are points at 96 DPI, so 12 draws at 16px, and tgui's sizes are pixels, so everything
    ///         here is converted to points, drawn at a fraction of the scale where whole points aren't enough
    ///         (<see cref="Quantize"/>). Markup then needs one more thing. <c>FontTag.GetSizeForFontTag</c> reads a
    ///         nested tag's size from <c>Stack[0]</c>, so that has to report pixels too, or <c>[bold]</c> would be converted
    ///         a second time and shrink. A <see cref="StackedFont"/> takes its metrics from the font it was built with and
    ///         not from <c>Stack[0]</c>, so <c>Stack[0]</c> is swapped afterwards for an empty font of the pixel size. It has
    ///         no glyphs, so drawing falls through it to the real font behind.
    /// </remarks>
    public Font GetFont(int size, FontKind kind = FontKind.Regular)
    {
        return BuildFont(size, kind, mono: false);
    }

    /// <summary>
    ///     tgui's <c>--font-family-mono</c>, <c>Consolas, monospace</c>: the player's own Consolas where they have one and
    ///         bundled Roboto Mono where they don't. Used by <c>Dialog</c> and by an <c>Input</c> with <c>monospace</c>.
    ///         Same sizes and fallbacks as <see cref="GetFont"/>.
    /// </summary>
    public Font GetMonoFont(int size, FontKind kind = FontKind.Regular)
    {
        return BuildFont(size, kind, mono: true);
    }

    private Font BuildFont(int size, FontKind kind, bool mono)
    {
        var cache = mono ? _monoFonts : _fonts;
        if (cache.TryGetValue((kind, size), out var font))
            return font;

        var (pointSize, factor) = Quantize(size);
        var primary = new KsTguiScaledFont(LoadPrimary(kind, pointSize, mono), factor, browserLineBox: true);

        var stack = new Font[FallbackPaths.Length + 2];
        stack[0] = primary;
        stack[1] = primary;
        for (var i = 0; i < FallbackPaths.Length; i++)
        {
            stack[i + 2] = new KsTguiScaledFont(
                _resourceCache.GetFont(FallbackPaths[i], pointSize), factor, browserLineBox: false);
        }

        font = new StackedFont(stack);
        stack[0] = _resourceCache.GetFont(BundledRoot + SizeCarrierFileName, size);

        cache[(kind, size)] = font;
        return font;
    }

    /// <summary>
    ///     A size in whole points, and the fraction of the scale to draw it at, that together make a font this many pixels
    ///         tall. A pixel is three quarters of a point, so a size divisible by four is exact as it is, an even one is
    ///         exact at half the scale, and any other at a quarter of it.
    /// </summary>
    public static (int PointSize, float Factor) Quantize(int pixelSize)
    {
        var divisor = pixelSize % 4 == 0 ? 1 : pixelSize % 2 == 0 ? 2 : 4;
        return (Math.Max(1, pixelSize * 3 * divisor / 4), 1f / (float)divisor);
    }

    /// <summary>
    ///     Font Awesome Solid at this size, for <c>Controls/KsTguiIcon</c>. Bundled and never a system font: its glyphs sit in
    ///         the private use area, so no other face could stand in for it.
    /// </summary>
    public Font GetIconFont(int size)
    {
        return GetIconFont(size, regular: false);
    }

    /// <summary>
    ///     As above, in Font Awesome Regular when <paramref name="regular"/>: tgui's <c>-o</c> icons. // KS14
    /// </summary>
    public Font GetIconFont(int size, bool regular)
    {
        var cache = regular ? _regularIconFonts : _iconFonts;
        if (cache.TryGetValue(size, out var font))
            return font;

        var (pointSize, factor) = Quantize(size);
        font = new KsTguiScaledFont(
            _resourceCache.GetFont(BundledRoot + (regular ? RegularIconFontFileName : IconFontFileName), pointSize), factor, browserLineBox: false);
        cache[size] = font;
        return font;
    }

    private readonly Dictionary<int, Font> _regularIconFonts = new();

    /// <summary>
    ///     Whether the player's own Verdana is in use, rather than the bundled fallback.
    /// </summary>
    public bool UsingSystemVerdana => (_systemFaces ??= FindSystemFaces(mono: false)).Count > 0;

    private Font LoadPrimary(FontKind kind, int size, bool mono)
    {
        var faces = mono
            ? _systemMonoFaces ??= FindSystemFaces(mono: true)
            : _systemFaces ??= FindSystemFaces(mono: false);

        return faces.TryGetValue(kind, out var face)
            ? face.Load(size)
            : _resourceCache.GetFont(mono ? MonoBundledPath(kind) : BundledRoot + BundledFileName(kind), size);
    }

    private Dictionary<FontKind, ISystemFontFace> FindSystemFaces(bool mono)
    {
        var faces = new Dictionary<FontKind, ISystemFontFace>();
        if (!_systemFontManager.IsSupported)
            return faces;

        var systemFaces = _systemFontManager.SystemFontFaces.ToList();
        foreach (var kind in Kinds)
        {
            if (SelectFace(systemFaces, face => face.PostscriptName, kind, mono: mono) is { } match)
                faces[kind] = match;
        }

        return faces;
    }

    /// <summary>
    ///     Picks the Verdana face of this kind out of a list, or null. Matched on the PostScript name because family names
    ///         are localised to the player's locale. Generic because only the engine can make an
    ///         <see cref="ISystemFontFace"/>, and this needs testing without one.
    /// </summary>
    public static T? SelectFace<T>(IEnumerable<T> faces, Func<T, string> postscriptName, FontKind kind, bool mono = false) where T : class
    {
        var wanted = mono ? MonoPostscriptNameFor(kind) : PostscriptNameFor(kind);
        return faces.FirstOrDefault(face => postscriptName(face) == wanted);
    }

    public static string PostscriptNameFor(FontKind kind)
    {
        return kind switch
        {
            FontKind.Bold => "Verdana-Bold",
            FontKind.Italic => "Verdana-Italic",
            FontKind.BoldItalic => "Verdana-BoldItalic",
            _ => "Verdana",
        };
    }

    /// <summary>
    ///     The PostScript name of Consolas in this kind, tgui's <c>--font-family-mono</c>.
    /// </summary>
    public static string MonoPostscriptNameFor(FontKind kind)
    {
        return kind switch
        {
            FontKind.Bold => "Consolas-Bold",
            FontKind.Italic => "Consolas-Italic",
            FontKind.BoldItalic => "Consolas-BoldItalic",
            _ => "Consolas",
        };
    }

    /// <summary>
    ///     The bundled stand-in for Consolas. Roboto Mono has no bold italic, so that is its bold.
    /// </summary>
    public static string MonoBundledPath(FontKind kind)
    {
        return "/Fonts/RobotoMono/" + kind switch
        {
            FontKind.Bold or FontKind.BoldItalic => "RobotoMono-Bold.ttf",
            FontKind.Italic => "RobotoMono-Italic.ttf",
            _ => "RobotoMono-Regular.ttf",
        };
    }

    public static string BundledFileName(FontKind kind)
    {
        return kind switch
        {
            FontKind.Bold => "DejaVuSans-Bold.ttf",
            FontKind.Italic => "DejaVuSans-Oblique.ttf",
            FontKind.BoldItalic => "DejaVuSans-BoldOblique.ttf",
            _ => "DejaVuSans.ttf",
        };
    }
}
