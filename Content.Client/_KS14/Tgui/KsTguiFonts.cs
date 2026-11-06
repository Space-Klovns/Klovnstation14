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
    ///     A font of this size and kind, with the Noto stack behind it for glyphs the primary lacks.
    /// </summary>
    public Font GetFont(int size, FontKind kind = FontKind.Regular)
    {
        if (_fonts.TryGetValue((kind, size), out var font))
            return font;

        // Flat, with the primary first and unwrapped: FontTag.GetSizeForFontTag only reads a size from a VectorFont at
        //      Stack[0], so anything else there silently drops markup sizes to 12.
        var stack = new Font[FallbackPaths.Length + 1];
        stack[0] = LoadPrimary(kind, size);
        for (var i = 0; i < FallbackPaths.Length; i++)
            stack[i + 1] = _resourceCache.GetFont(FallbackPaths[i], size);

        font = new StackedFont(stack);
        _fonts[(kind, size)] = font;
        return font;
    }

    /// <summary>
    ///     Font Awesome Solid at this size, for <c>Controls/KsTguiIcon</c>. Bundled and never a system font: its glyphs sit in
    ///         the private use area, so no other face could stand in for it.
    /// </summary>
    public Font GetIconFont(int size)
    {
        return _resourceCache.GetFont(BundledRoot + IconFontFileName, size);
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
