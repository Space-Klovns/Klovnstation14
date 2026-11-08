using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The clicks on a tgui scrollbar's arrows. They are drawn by the bar's track, outside the bar, so the engine's bar never
///         hears about them: this finds the bar under the mouse and does what a browser does. A press scrolls a line, and
///         holding it scrolls a line every 50ms after a quarter of a second.
/// </summary>
/// <remarks>
///     Driven by <see cref="KsTguiFeelUIController"/> each frame with the mouse's state, as the engine has no event for a
///         click that lands on nothing in particular. Any bar drawn with a <see cref="KsTguiScrollTrackStyleBox"/> counts: a
///         scroll container's, or a text area's thin one.
/// </remarks>
public sealed class KsTguiScrollArrows
{
    private bool _wasDown;
    private ScrollBar? _heldBar;
    private bool _heldEnd;
    private float _heldTime;
    private float _nextRepeat;

    /// <summary>
    ///     Whether a press or hold is being handled, for a test.
    /// </summary>
    public bool Holding => _heldBar != null;

    /// <param name="hovered">The control under the mouse, or something inside it.</param>
    /// <param name="mouseDown">Whether the primary button is down.</param>
    /// <param name="mousePosition">The mouse in UI units.</param>
    public void Update(Control? hovered, bool mouseDown, Vector2 mousePosition, float deltaSeconds)
    {
        var pressed = mouseDown && !_wasDown;
        _wasDown = mouseDown;

        if (!mouseDown)
        {
            _heldBar = null;
            return;
        }

        if (pressed)
        {
            _heldBar = null;
            if (FindArrow(hovered, mousePosition, out var bar, out var end))
            {
                _heldBar = bar;
                _heldEnd = end;
                _heldTime = 0f;
                _nextRepeat = KsTguiScrollBarGeometry.RepeatDelay;
                Scroll(bar, end);
            }

            return;
        }

        if (_heldBar is not { IsInsideTree: true } held)
        {
            _heldBar = null;
            return;
        }

        // Out of its cell the arrow stops, and starts again on coming back, as a browser's does.
        if (!InCell(held, _heldEnd, mousePosition))
            return;

        _heldTime += deltaSeconds;
        while (_heldTime >= _nextRepeat)
        {
            Scroll(held, _heldEnd);
            _nextRepeat += KsTguiScrollBarGeometry.RepeatInterval;
        }
    }

    /// <summary>
    ///     An array rather than a span literal: that compiles to a pointer into static data, which the sandbox's ILVerify refuses.
    /// </summary>
    private static readonly bool[] Both = [false, true];

    private static bool FindArrow(Control? hovered, Vector2 mousePosition, out ScrollBar bar, out bool end)
    {
        // Innermost first: a scrolled box inside another covers the outer one's arrows where they overlap.
        for (var current = hovered; current != null; current = current.Parent)
        {
            foreach (var child in current.Children)
            {
                if (child is not ScrollBar candidate)
                    continue;

                foreach (var candidateEnd in Both)
                {
                    if (!InCell(candidate, candidateEnd, mousePosition))
                        continue;

                    bar = candidate;
                    end = candidateEnd;
                    return true;
                }
            }
        }

        bar = null!;
        end = false;
        return false;
    }

    /// <summary>
    ///     Whether the point is in one of this bar's arrow cells, if the bar is showing and tgui's.
    /// </summary>
    public static bool InCell(ScrollBar bar, bool end, Vector2 point)
    {
        if (!bar.VisibleInTree
            || !bar.TryGetStyleProperty<Robust.Client.Graphics.StyleBox>(ScrollBar.StylePropertyTrack, out var style)
            || style is not KsTguiScrollTrackStyleBox track)
        {
            return false;
        }

        var barBox = UIBox2.FromDimensions(bar.GlobalPosition, bar.Size);
        return track.Metrics.Cell(barBox, bar is HScrollBar, end, scale: 1f).Contains(point);
    }

    private static void Scroll(ScrollBar bar, bool end)
    {
        var step = end ? KsTguiScrollBarGeometry.ArrowStep : -KsTguiScrollBarGeometry.ArrowStep;

        // A scroll container's bar counts in UI units; the engine's other owners of a bar (TextEdit, ItemList, OutputPanel,
        //      Tree) count theirs in pixels.
        if (bar.Parent is not ScrollContainer)
            step *= bar.UIScale;

        bar.ValueTarget += step;
    }
}
