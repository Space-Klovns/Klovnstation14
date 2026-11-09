using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Tgui.TextSelection;

/// <summary>
///     One rune as drawn: its box in pixels, relative to the top left of the layout it belongs to.
/// </summary>
public readonly record struct KsGlyph(Rune Rune, UIBox2 Box, int Line);

/// <summary>
///     Where a control puts each rune of its text. The engine has selection only in its text boxes, and the layout of
///         <c>Label</c>, <c>RichTextLabel</c> and <c>OutputPanel</c> is private to them, so this works the same positions out again
///         from the same inputs. <see cref="KsTextLayouts.Rich"/> is a port of <c>RichTextEntry.Update</c> (the wrapping) and
///         <c>RichTextEntry.DrawPass</c> (the positions), MIT, Space Wizards, and <see cref="KsTextLayouts.ForLabel"/> follows
///         <c>Label.Draw</c>. <c>KsTextLayoutTests</c> holds them to what the engine measures.
/// </summary>
public sealed class KsTextLayout
{
    public readonly List<KsGlyph> Glyphs = new();

    /// <summary>
    ///     The rune indices the engine breaks lines at, as its own <c>RichTextEntry.LineBreaks</c>.
    /// </summary>
    public readonly List<int> LineBreaks = new();

    /// <summary>
    ///     The width and height the engine reports for the same text (<c>RichTextEntry.Width</c> and <c>Height</c> for rich text,
    ///         <c>Label</c>'s measured size for a label), in pixels.
    /// </summary>
    public int Width;

    public int Height;

    /// <summary>
    ///     The number of lines drawn.
    /// </summary>
    public int LineCount;

    /// <summary>
    ///     First glyph of each line, plus one more entry for the end.
    /// </summary>
    private int[]? _lineStarts;

    public string Text
    {
        get
        {
            var builder = new StringBuilder(Glyphs.Count);
            foreach (var glyph in Glyphs)
                builder.Append(glyph.Rune.ToString());

            return builder.ToString();
        }
    }

    /// <summary>
    ///     The index of the first glyph of <paramref name="line"/>, and of the first glyph past it for the line after the last.
    /// </summary>
    public int LineStart(int line)
    {
        EnsureLineStarts();
        return _lineStarts![Math.Clamp(line, 0, _lineStarts.Length - 1)];
    }

    public int LineTop(int line)
    {
        var start = LineStart(line);
        return start < Glyphs.Count ? (int)Glyphs[start].Box.Top : Height;
    }

    public int LineBottom(int line)
    {
        var start = LineStart(line);
        return start < Glyphs.Count ? (int)Glyphs[start].Box.Bottom : Height;
    }

    private void EnsureLineStarts()
    {
        if (_lineStarts != null)
            return;

        var starts = new List<int>();
        var lastLine = -1;
        for (var i = 0; i < Glyphs.Count; i++)
        {
            while (lastLine < Glyphs[i].Line)
            {
                starts.Add(i);
                lastLine++;
            }
        }

        starts.Add(Glyphs.Count);
        _lineStarts = starts.ToArray();
    }

    internal void Seal()
    {
        _lineStarts = null;
        EnsureLineStarts();
        LineCount = _lineStarts!.Length - 1;
    }
}

public static class KsTextLayouts
{
    private static readonly Rune Newline = new('\n');
    private static readonly Rune Space = new(' ');

    /// <summary>
    ///     <c>Label</c>'s text: its font, <see cref="Label.Align"/>, <see cref="Label.VAlign"/> and size decide each rune's box
    ///         (<c>Label.Draw</c>). Null when the label has no string to read.
    /// </summary>
    public static KsTextLayout? ForLabel(Label label, Font font)
    {
        string? text;
        try
        {
            text = label.Text;
        }
        catch (InvalidOperationException)
        {
            // Text set through TextMemory has no string to hand out.
            return null;
        }

        var layout = new KsTextLayout();
        if (string.IsNullOrEmpty(text))
        {
            layout.Seal();
            return layout;
        }

        var scale = label.UIScale;
        var lineHeight = font.GetLineHeight(scale);
        var ascent = font.GetAscent(scale);
        var size = label.PixelSize;

        // First the widths of the lines, which the alignment needs (Label._calculateTextDimension).
        var widths = new List<int> { 0 };
        var height = font.GetHeight(scale);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune == Newline)
            {
                widths.Add(0);
                height += lineHeight;
            }
            else if (font.TryGetCharMetrics(rune, scale, out var metrics))
            {
                widths[^1] += metrics.Advance;
            }
        }

        var widest = 0;
        foreach (var width in widths)
            widest = Math.Max(widest, width);

        layout.Width = label.ClipText ? 0 : widest;
        layout.Height = height;

        int verticalOffset;
        switch (label.VAlign)
        {
            case Label.VAlignMode.Top:
                verticalOffset = 0;
                break;
            case Label.VAlignMode.Bottom:
                verticalOffset = size.Y - height;
                break;
            default:
                verticalOffset = (size.Y - height) / 2;
                break;
        }

        var align = label.Align;
        int HorizontalOffset(int line)
        {
            return align switch
            {
                Label.AlignMode.Center or Label.AlignMode.Fill => (size.X - widths[line]) / 2,
                Label.AlignMode.Right => size.X - widths[line],
                _ => 0,
            };
        }

        var lineIndex = 0;
        float x = HorizontalOffset(0);
        foreach (var rune in text.EnumerateRunes())
        {
            var top = verticalOffset + lineHeight * lineIndex;
            if (rune == Newline)
            {
                layout.Glyphs.Add(new KsGlyph(rune, UIBox2.FromDimensions(x, top, 0f, lineHeight), lineIndex));
                lineIndex++;
                x = HorizontalOffset(lineIndex);
                continue;
            }

            var advance = font.TryGetCharMetrics(rune, scale, out var metrics) ? metrics.Advance : 0;
            layout.Glyphs.Add(new KsGlyph(rune, UIBox2.FromDimensions(x, top, advance, lineHeight), lineIndex));
            x += advance;
        }

        _ = ascent;
        layout.Seal();
        return layout;
    }

    /// <summary>
    ///     A <see cref="FormattedMessage"/> wrapped at <paramref name="maxSizeX"/> pixels, as <c>RichTextEntry</c> does for
    ///         <c>RichTextLabel</c> and for each entry of an <c>OutputPanel</c>.
    /// </summary>
    /// <param name="owner">The control the text belongs to; its style properties reach the tags.</param>
    /// <param name="tagsAllowed">The tags the control allows, null for all.</param>
    /// <param name="realControls">
    ///     The controls the control itself holds for the message's tags that make one (a link), in the order of their nodes,
    ///         when they exist. Without them a stand-in is made and measured for each.
    /// </param>
    public static KsTextLayout Rich(
        FormattedMessage message,
        Control owner,
        MarkupTagManager tagManager,
        Type[]? tagsAllowed,
        Font defaultFont,
        float maxSizeX,
        float uiScale,
        float lineHeightScale = 1f,
        IReadOnlyList<Control>? realControls = null)
    {
        var layout = new KsTextLayout();
        var controlWidths = GetControlSizes(message, tagManager, tagsAllowed, maxSizeX, uiScale, realControls);

        // The wrapping: RichTextEntry.Update.
        var height = defaultFont.GetHeight(uiScale);
        var wordWrap = new KsWordWrap(maxSizeX);
        var context = new MarkupDrawingContext(owner);
        context.Font.Push(defaultFont);
        context.Color.Push(Color.White);

        var aborted = false;
        var nodeIndex = -1;
        foreach (var node in message)
        {
            nodeIndex++;
            var text = ProcessNode(tagManager, tagsAllowed, node, context);

            if (!context.Font.TryPeek(out var font))
                font = defaultFont;

            foreach (var rune in text.EnumerateRunes())
            {
                wordWrap.NextRune(rune, out var breakLine, out var breakNewLine, out var skip);
                CheckLineBreak(breakLine);
                CheckLineBreak(breakNewLine);

                if (skip)
                    continue;

                if (!font.TryGetCharMetrics(rune, uiScale, out var metrics))
                    continue;

                wordWrap.NextMetrics(metrics.Advance, out breakLine, out var abort);
                CheckLineBreak(breakLine);
                if (abort)
                {
                    aborted = true;
                    break;
                }
            }

            if (aborted)
                break;

            if (!controlWidths.TryGetValue(nodeIndex, out var controlSize))
                continue;

            wordWrap.NextMetrics(controlSize.X, out var controlBreak, out var controlAbort);
            CheckLineBreak(controlBreak);
            if (controlAbort)
            {
                aborted = true;
                break;
            }
        }

        if (!aborted)
        {
            layout.Width = wordWrap.FinalizeText(out var finalBreak);
            CheckLineBreak(finalBreak);
        }

        layout.Height = height;

        void CheckLineBreak(int? line)
        {
            if (line is not { } l)
                return;

            layout.LineBreaks.Add(l);
            if (!context.Font.TryPeek(out var current))
                current = defaultFont;

            height += GetLineHeight(current, uiScale, lineHeightScale);
            layout.Height = height;
        }

        // The positions: RichTextEntry.DrawPass.
        PlaceGlyphs(layout, message, owner, tagManager, tagsAllowed, defaultFont, uiScale, lineHeightScale, controlWidths);
        layout.Seal();
        return layout;
    }

    private static void PlaceGlyphs(
        KsTextLayout layout,
        FormattedMessage message,
        Control owner,
        MarkupTagManager tagManager,
        Type[]? tagsAllowed,
        Font defaultFont,
        float uiScale,
        float lineHeightScale,
        Dictionary<int, Vector2i> controlSizes)
    {
        var context = new MarkupDrawingContext(owner);
        context.Color.Push(Color.White);
        context.Font.Push(defaultFont);

        var globalBreakCounter = 0;
        var lineBreakIndex = 0;
        var lineIndex = 0;
        var baseLineX = 0f;
        var baseLineY = (float)defaultFont.GetAscent(uiScale);
        var controlYAdvance = 0f;
        var invertedScale = 1f / uiScale;

        var nodeIndex = -1;
        foreach (var node in message)
        {
            nodeIndex++;
            var text = ProcessNode(tagManager, tagsAllowed, node, context);
            if (!context.Font.TryPeek(out var font))
                font = defaultFont;

            foreach (var rune in text.EnumerateRunes())
            {
                var skipSpaceBaseline = false;

                if (lineBreakIndex < layout.LineBreaks.Count && layout.LineBreaks[lineBreakIndex] == globalBreakCounter)
                {
                    baseLineX = 0f;
                    baseLineY += GetLineHeight(font, uiScale, lineHeightScale) + controlYAdvance;
                    controlYAdvance = 0f;
                    lineBreakIndex++;
                    lineIndex++;

                    // The engine draws the space a line broke at on the new line without moving on, so it has no width there.
                    if (rune == Space)
                        skipSpaceBaseline = true;
                }

                var advance = font.TryGetCharMetrics(rune, uiScale, out var metrics) ? metrics.Advance : 0;
                var top = baseLineY - font.GetAscent(uiScale);
                layout.Glyphs.Add(new KsGlyph(
                    rune,
                    UIBox2.FromDimensions(baseLineX, top, skipSpaceBaseline ? 0f : advance, GetLineHeight(font, uiScale, lineHeightScale)),
                    lineIndex));

                if (!skipSpaceBaseline)
                    baseLineX += advance;

                globalBreakCounter++;
            }

            if (!controlSizes.TryGetValue(nodeIndex, out var controlSize))
                continue;

            controlYAdvance = Math.Max(0f, (controlSize.Y - GetLineHeight(font, uiScale, lineHeightScale)) * invertedScale);
            baseLineX += controlSize.X;
        }
    }

    /// <summary>
    ///     The pixel size of the control each of the message's nodes makes, if it makes one. The real controls are only read, since
    ///         they were measured by the control that holds them.
    /// </summary>
    private static Dictionary<int, Vector2i> GetControlSizes(
        FormattedMessage message,
        MarkupTagManager tagManager,
        Type[]? tagsAllowed,
        float maxSizeX,
        float uiScale,
        IReadOnlyList<Control>? realControls)
    {
        var sizes = new Dictionary<int, Vector2i>();
        var nodeIndex = -1;
        var realIndex = 0;
        foreach (var node in message)
        {
            nodeIndex++;

            if (node.Name == null
                || !tagManager.TryGetMarkupTagHandler(node.Name, tagsAllowed, out var handler)
                || !handler.TryCreateControl(node, out var standIn))
            {
                continue;
            }

            if (realControls != null && realIndex < realControls.Count)
            {
                sizes[nodeIndex] = realControls[realIndex++].DesiredPixelSize;
                continue;
            }

            realIndex++;
            standIn.Measure(new Vector2(maxSizeX * (1f / uiScale), float.PositiveInfinity));
            sizes[nodeIndex] = new Vector2i(
                (int)MathF.Ceiling(standIn.DesiredSize.X * uiScale),
                (int)MathF.Ceiling(standIn.DesiredSize.Y * uiScale));
        }

        return sizes;
    }

    private static string ProcessNode(MarkupTagManager tagManager, Type[]? tagsAllowed, MarkupNode node, MarkupDrawingContext context)
    {
        if (node.Name == null)
            return node.Value.StringValue ?? "";

        if (!tagManager.TryGetMarkupTagHandler(node.Name, tagsAllowed, out var tag))
            return "";

        if (!node.Closing)
        {
            tag.PushDrawContext(node, context);
            return tag.TextBefore(node);
        }

        tag.PopDrawContext(node, context);
        return tag.TextAfter(node);
    }

    private static int GetLineHeight(Font font, float uiScale, float lineHeightScale)
    {
        var height = font.GetLineHeight(uiScale);
        return (int)(height * lineHeightScale);
    }
}
