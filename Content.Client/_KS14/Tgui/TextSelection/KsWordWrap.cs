using System;
using System.Text;

namespace Content.Client._KS14.Tgui.TextSelection;

/// <summary>
///     The engine's <c>WordWrap</c> (<c>Robust.Client/UserInterface/WordWrap.cs</c>, MIT, Space Wizards), which is internal,
///         with the same arithmetic so that <see cref="KsTextLayout"/> breaks lines exactly where
///         <c>RichTextLabel</c> and <c>OutputPanel</c> do. It takes an advance in place of <c>CharMetrics</c>, and gives up
///         quietly where the engine throws: a selection layout must never be the thing that breaks a window.
/// </summary>
internal struct KsWordWrap
{
    private readonly float _maxSizeX;

    public float MaxUsedWidth;

    // Index we put into the line breaks list when a line break should occur.
    public int BreakIndexCounter;
    public int NextBreakIndexCounter;

    // If the current word ends up too long, this is the index to put a line break.
    public (int index, float lineSize)? WordStartBreakIndex;
    public int WordSizePixels;
    public int PosX;
    public Rune LastRune;

    public KsWordWrap(float maxSizeX)
    {
        this = default;
        _maxSizeX = maxSizeX;
        LastRune = new Rune('A');
    }

    public void NextRune(Rune rune, out int? breakLine, out int? breakNewLine, out bool skip)
    {
        BreakIndexCounter = NextBreakIndexCounter;
        NextBreakIndexCounter += 1;

        breakLine = null;
        breakNewLine = null;
        skip = false;

        if (IsWordBoundary(LastRune, rune) || rune == new Rune('\n'))
        {
            if (PosX > _maxSizeX && LastRune != new Rune(' '))
            {
                if (!WordStartBreakIndex.HasValue)
                    return;

                breakLine = WordStartBreakIndex.Value.index;
                MaxUsedWidth = Math.Max(MaxUsedWidth, WordStartBreakIndex.Value.lineSize);
                PosX = WordSizePixels;
            }

            WordSizePixels = 0;
            WordStartBreakIndex = (BreakIndexCounter, PosX);

            if (rune == new Rune('\n'))
            {
                MaxUsedWidth = Math.Max(MaxUsedWidth, PosX);
                PosX = 0;
                WordStartBreakIndex = null;
                skip = true;
                breakNewLine = BreakIndexCounter;
            }
        }

        LastRune = rune;
    }

    public void NextMetrics(int advance, out int? breakLine, out bool abort)
    {
        abort = false;
        breakLine = null;

        var oldWordSizePixels = WordSizePixels;
        WordSizePixels += advance;
        PosX += advance;

        if (PosX <= _maxSizeX)
            return;

        if (WordStartBreakIndex.HasValue && oldWordSizePixels != 0)
        {
            breakLine = WordStartBreakIndex.Value.index;
            MaxUsedWidth = Math.Max(MaxUsedWidth, WordStartBreakIndex.Value.lineSize);
            PosX = WordSizePixels;
        }

        if (WordSizePixels > _maxSizeX)
        {
            if (oldWordSizePixels == 0)
            {
                abort = true;
                return;
            }

            breakLine = BreakIndexCounter;
            WordSizePixels -= oldWordSizePixels;
            WordStartBreakIndex = null;
            MaxUsedWidth = Math.Max(MaxUsedWidth, _maxSizeX);
            PosX = WordSizePixels;
        }
    }

    public int FinalizeText(out int? breakLine)
    {
        if (PosX > _maxSizeX)
        {
            if (!WordStartBreakIndex.HasValue)
            {
                breakLine = null;
                return (int)MaxUsedWidth;
            }

            breakLine = WordStartBreakIndex.Value.index;
            MaxUsedWidth = Math.Max(MaxUsedWidth, WordStartBreakIndex.Value.lineSize);
        }
        else
        {
            breakLine = null;
            MaxUsedWidth = Math.Max(MaxUsedWidth, PosX);
        }

        return (int)MaxUsedWidth;
    }

    private static bool IsWordBoundary(Rune a, Rune b)
    {
        return a == new Rune(' ') || b == new Rune(' ') || a == new Rune('-') || b == new Rune('-');
    }
}
