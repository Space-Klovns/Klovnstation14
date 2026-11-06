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

    /// <summary>
    ///     A font with no glyphs and no height. See <see cref="GetFont"/>.
    /// </summary>
    private const string SizeCarrierFileName = "size_carrier.ttf";

    /// <summary>
    ///     The smallest size in points, so that upstream's small print stays legible.
    /// </summary>
    private const int MinPointSize = 7;

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
    private Dictionary<FontKind, ISystemFontFace>? _systemFaces;

    /// <summary>
    ///     A font of this size in pixels and kind, with the Noto stack behind it for glyphs the primary lacks.
    /// </summary>
    /// <remarks>
    ///     The engine's font sizes are points at 96 DPI, so 12 draws at 16px, and tgui's sizes are pixels, so everything
    ///         here is converted to points. Markup then needs one more thing. <c>FontTag.GetSizeForFontTag</c> reads a
    ///         nested tag's size from <c>Stack[0]</c>, so that has to report pixels too, or <c>[bold]</c> would be converted
    ///         a second time and shrink. A <see cref="StackedFont"/> takes its metrics from the font it was built with and
    ///         not from <c>Stack[0]</c>, so <c>Stack[0]</c> is swapped afterwards for an empty font of the pixel size. It has
    ///         no glyphs, so drawing falls through it to the real font behind.
    /// </remarks>
    public Font GetFont(int size, FontKind kind = FontKind.Regular)
    {
        if (_fonts.TryGetValue((kind, size), out var font))
            return font;

        var pointSize = PointSize(size);
        var primary = LoadPrimary(kind, pointSize);

        var stack = new Font[FallbackPaths.Length + 2];
        stack[0] = primary;
        stack[1] = primary;
        for (var i = 0; i < FallbackPaths.Length; i++)
            stack[i + 2] = _resourceCache.GetFont(FallbackPaths[i], pointSize);

        font = new StackedFont(stack);
        stack[0] = _resourceCache.GetFont(BundledRoot + SizeCarrierFileName, size);

        _fonts[(kind, size)] = font;
        return font;
    }

    /// <summary>
    ///     The engine's size for a font this many pixels tall.
    /// </summary>
    public static int PointSize(int pixelSize)
    {
        return Math.Max(MinPointSize, (int)MathF.Round((float)pixelSize * 0.75f, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    ///     Font Awesome Solid at this size, for <c>Controls/KsTguiIcon</c>. Bundled and never a system font: its glyphs sit in
    ///         the private use area, so no other face could stand in for it.
    /// </summary>
    public Font GetIconFont(int size)
    {
        return _resourceCache.GetFont(BundledRoot + IconFontFileName, PointSize(size));
    }

    /// <summary>
    ///     Whether the player's own Verdana is in use, rather than the bundled fallback.
    /// </summary>
    public bool UsingSystemVerdana => (_systemFaces ??= FindSystemFaces()).Count > 0;

    private Font LoadPrimary(FontKind kind, int size)
    {
        _systemFaces ??= FindSystemFaces();

        return _systemFaces.TryGetValue(kind, out var face)
            ? face.Load(size)
            : _resourceCache.GetFont(BundledRoot + BundledFileName(kind), size);
    }

    private Dictionary<FontKind, ISystemFontFace> FindSystemFaces()
    {
        var faces = new Dictionary<FontKind, ISystemFontFace>();
        if (!_systemFontManager.IsSupported)
            return faces;

        var systemFaces = _systemFontManager.SystemFontFaces.ToList();
        foreach (var kind in Kinds)
        {
            if (SelectFace(systemFaces, face => face.PostscriptName, kind) is { } match)
                faces[kind] = match;
        }

        return faces;
    }

    /// <summary>
    ///     Picks the Verdana face of this kind out of a list, or null. Matched on the PostScript name because family names
    ///         are localised to the player's locale. Generic because only the engine can make an
    ///         <see cref="ISystemFontFace"/>, and this needs testing without one.
    /// </summary>
    public static T? SelectFace<T>(IEnumerable<T> faces, Func<T, string> postscriptName, FontKind kind) where T : class
    {
        var wanted = PostscriptNameFor(kind);
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
