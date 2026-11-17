using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Robust.Shared.Maths;

namespace Content.Client._KS14.Tgui.TextSelection;

/// <summary>
///     The text of one control, as one or more <see cref="KsTextLayout"/>s stacked down a page: one for a label, one for
///         each message of the chat. A caret is a number from zero to <see cref="GlyphCount"/> counted across all of them, and
///         a selection is two carets. Everything is in the pixels of the page, which the control maps onto the screen.
/// </summary>
public sealed class KsTextDocument
{
    public readonly record struct Part(KsTextLayout Layout, Vector2 Origin, int FirstGlyph);

    public readonly List<Part> Parts = new();

    public int GlyphCount { get; private set; }

    public void Add(KsTextLayout layout, Vector2 origin)
    {
        Parts.Add(new Part(layout, origin, GlyphCount));
        GlyphCount += layout.Glyphs.Count;
    }

    private int PartOf(int glyphIndex)
    {
        // Binary search for the last part that starts at or before the glyph.
        var low = 0;
        var high = Parts.Count - 1;
        var found = 0;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (Parts[middle].FirstGlyph <= glyphIndex)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    /// <summary>
    ///     The caret nearest a point on the page: the gap between two runes, or before the first or after the last.
    /// </summary>
    public int CaretAt(Vector2 point)
    {
        if (Parts.Count == 0)
            return 0;

        foreach (var part in Parts)
        {
            var layout = part.Layout;
            var localY = point.Y - part.Origin.Y;

            if (localY < 0f)
                return part.FirstGlyph;

            if (localY >= layout.Height || layout.Glyphs.Count == 0)
                continue;

            var line = 0;
            while (line < layout.LineCount - 1 && localY >= layout.LineBottom(line))
                line++;

            return part.FirstGlyph + CaretInLine(layout, line, point.X - part.Origin.X);
        }

        return GlyphCount;
    }

    private static int CaretInLine(KsTextLayout layout, int line, float localX)
    {
        var start = layout.LineStart(line);
        var end = layout.LineStart(line + 1);

        // A label's newline is the last rune of its own line; the caret never goes past it.
        if (end > start && layout.Glyphs[end - 1].Rune.Value == '\n')
            end--;

        for (var i = start; i < end; i++)
        {
            var box = layout.Glyphs[i].Box;
            if (localX < (box.Left + box.Right) / 2f)
                return i;
        }

        return end;
    }

    /// <summary>
    ///     The rune a point is over, for picking a word; -1 over none.
    /// </summary>
    public int GlyphAt(Vector2 point)
    {
        var caret = CaretAt(point);
        if (GlyphCount == 0)
            return -1;

        // The caret sits before the rune it is nearest to, unless it is at the very end.
        return Math.Min(caret, GlyphCount - 1);
    }

    /// <summary>
    ///     The run of the same kind of rune (letters and digits, spaces, or punctuation) around a rune, as a double click
    ///         selects it.
    /// </summary>
    public (int Start, int End) WordAt(int glyphIndex)
    {
        if (glyphIndex < 0 || glyphIndex >= GlyphCount)
            return (0, 0);

        var partIndex = PartOf(glyphIndex);
        var part = Parts[partIndex];
        var glyphs = part.Layout.Glyphs;
        var local = glyphIndex - part.FirstGlyph;
        var kind = Classify(glyphs[local].Rune);

        var start = local;
        while (start > 0 && Classify(glyphs[start - 1].Rune) == kind)
            start--;

        var end = local + 1;
        while (end < glyphs.Count && Classify(glyphs[end].Rune) == kind)
            end++;

        return (part.FirstGlyph + start, part.FirstGlyph + end);
    }

    private static int Classify(Rune rune)
    {
        if (Rune.IsLetterOrDigit(rune) || rune.Value == '_')
            return 0;

        return Rune.IsWhiteSpace(rune) ? 1 : 2;
    }

    /// <summary>
    ///     The boxes a selection covers on the page, one for each line of each message it touches.
    /// </summary>
    public void SelectionBoxes(int a, int b, List<UIBox2> into)
    {
        var start = Math.Clamp(Math.Min(a, b), 0, GlyphCount);
        var end = Math.Clamp(Math.Max(a, b), 0, GlyphCount);
        if (start >= end)
            return;

        foreach (var part in Parts)
        {
            var from = Math.Max(start, part.FirstGlyph) - part.FirstGlyph;
            var to = Math.Min(end, part.FirstGlyph + part.Layout.Glyphs.Count) - part.FirstGlyph;
            var glyphs = part.Layout.Glyphs;

            var i = from;
            while (i < to)
            {
                var line = glyphs[i].Line;
                var box = default(UIBox2);
                var any = false;
                while (i < to && glyphs[i].Line == line)
                {
                    var glyphBox = glyphs[i].Box;
                    if (glyphBox.Width > 0f)
                    {
                        box = any ? new UIBox2(
                            Math.Min(box.Left, glyphBox.Left), Math.Min(box.Top, glyphBox.Top),
                            Math.Max(box.Right, glyphBox.Right), Math.Max(box.Bottom, glyphBox.Bottom)) : glyphBox;
                        any = true;
                    }

                    i++;
                }

                if (any)
                    into.Add(box.Translated(part.Origin));
            }
        }
    }

    /// <summary>
    ///     The text of a selection. Soft-wrapped lines run on, as in a browser, and messages are told apart by a newline.
    /// </summary>
    public string TextBetween(int a, int b)
    {
        var start = Math.Clamp(Math.Min(a, b), 0, GlyphCount);
        var end = Math.Clamp(Math.Max(a, b), 0, GlyphCount);
        if (start >= end)
            return string.Empty;

        var builder = new StringBuilder();
        var first = true;
        foreach (var part in Parts)
        {
            var from = Math.Max(start, part.FirstGlyph) - part.FirstGlyph;
            var to = Math.Min(end, part.FirstGlyph + part.Layout.Glyphs.Count) - part.FirstGlyph;
            if (from >= to)
                continue;

            if (!first)
                builder.Append('\n');

            first = false;
            for (var i = from; i < to; i++)
                builder.Append(part.Layout.Glyphs[i].Rune.ToString());
        }

        return builder.ToString();
    }
}
