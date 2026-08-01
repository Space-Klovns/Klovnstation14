using System.Numerics;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Shuttles.UI;

/// <summary>
///     The HighFleet-style titled instrument box every KS console screen is built
///         from: a one-pixel border broken on its top edge by an all-caps title,
///         children inset inside. Colours come from the screen's
///         <c>KsInstrumentPalette</c> via <see cref="Accent"/>/<see cref="Background"/>
///         so one control serves every tab's cast.
/// </summary>
public sealed class KsInstrumentPanel : Control
{
    /// <summary>Content inset from the side/bottom border, in virtual pixels.</summary>
    private const float Pad = 8f;

    /// <summary>Content inset from the top border, leaving room for the title to break it.</summary>
    private const float TopPad = 16f;

    /// <summary>Where the title's gap starts along the top edge.</summary>
    private const float TitleIndent = 10f;

    private readonly Font _titleFont;

    /// <summary>The panel's title, drawn breaking the top border. Uppercased by convention, not by code.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Border and title colour.</summary>
    public Color Accent { get; set; } = Color.FromHex("#C8A030");

    /// <summary>Fill behind the panel. Transparent by default so the window background shows through.</summary>
    public Color Background { get; set; } = Color.Transparent;

    public KsInstrumentPanel()
    {
        _titleFont = IoCManager.Resolve<IResourceCache>()
            .GetFont(KsInstrumentSheetlet.FontPath, KsInstrumentSheetlet.FontSizeBody);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var inset = new Vector2(Pad * 2f, TopPad + Pad);
        var inner = Vector2.Max(Vector2.Zero, availableSize - inset);

        var max = Vector2.Zero;
        foreach (var child in Children)
        {
            child.Measure(inner);
            max = Vector2.Max(max, child.DesiredSize);
        }

        return max + inset;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var box = new UIBox2(
            Pad,
            TopPad,
            MathF.Max(Pad, finalSize.X - Pad),
            MathF.Max(TopPad, finalSize.Y - Pad));

        foreach (var child in Children)
        {
            child.Arrange(box);
        }

        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = (Vector2) PixelSize;
        if (size.X < 2f || size.Y < 2f)
            return;

        if (Background.A > 0f)
            handle.DrawRect(new UIBox2(Vector2.Zero, size), Background);

        // The border sits half the title height down so the title text straddles it.
        var titleDim = Title.Length > 0 ? handle.GetDimensions(_titleFont, Title, UIScale) : Vector2.Zero;
        var top = MathF.Max(1f, titleDim.Y / 2f);
        var left = 1f;
        var right = size.X - 1f;
        var bottom = size.Y - 1f;

        handle.DrawLine(new Vector2(left, top), new Vector2(left, bottom), Accent);
        handle.DrawLine(new Vector2(right, top), new Vector2(right, bottom), Accent);
        handle.DrawLine(new Vector2(left, bottom), new Vector2(right, bottom), Accent);

        if (Title.Length > 0)
        {
            var indent = TitleIndent * UIScale;
            var gapPad = 4f * UIScale;
            var gapStart = MathF.Min(indent, right);
            var gapEnd = MathF.Min(gapStart + titleDim.X + gapPad * 2f, right);

            // Top edge in two segments, broken around the title.
            handle.DrawLine(new Vector2(left, top), new Vector2(gapStart, top), Accent);
            handle.DrawLine(new Vector2(gapEnd, top), new Vector2(right, top), Accent);
            handle.DrawString(_titleFont, new Vector2(gapStart + gapPad, 0f), Title, UIScale, Accent);
        }
        else
        {
            handle.DrawLine(new Vector2(left, top), new Vector2(right, top), Accent);
        }
    }
}
