using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The clicks on a tgui scrollbar's arrows. They are drawn by the bar's track, outside the bar, so the engine's bar never
///         hears about them: this finds the scroll container under the mouse and does what a browser does. A press scrolls
///         a line, and holding it scrolls a line every 50ms after a quarter of a second.
/// </summary>
/// <remarks>
///     Driven by <see cref="KsTguiFeelUIController"/> each frame with the mouse's state, as the engine has no event for a
///         click that lands on nothing in particular.
/// </remarks>
public sealed class KsTguiScrollArrows
{
    private bool _wasDown;
    private ScrollContainer? _heldContainer;
    private bool _heldHorizontal;
    private bool _heldEnd;
    private float _heldTime;
    private float _nextRepeat;

    /// <summary>
    ///     Whether a press or hold is being handled, for a test.
    /// </summary>
    public bool Holding => _heldContainer != null;

    /// <param name="hovered">The control under the mouse, or something inside it.</param>
    /// <param name="mouseDown">Whether the primary button is down.</param>
    /// <param name="mousePosition">The mouse in UI units.</param>
    public void Update(Control? hovered, bool mouseDown, Vector2 mousePosition, float deltaSeconds)
    {
        var pressed = mouseDown && !_wasDown;
        _wasDown = mouseDown;

        if (!mouseDown)
        {
            _heldContainer = null;
            return;
        }

        if (pressed)
        {
            _heldContainer = null;
            if (FindArrow(hovered, mousePosition, out var container, out var horizontal, out var end))
            {
                _heldContainer = container;
                _heldHorizontal = horizontal;
                _heldEnd = end;
                _heldTime = 0f;
                _nextRepeat = KsTguiScrollBarGeometry.RepeatDelay;
                Scroll(container, horizontal, end);
            }

            return;
        }

        if (_heldContainer is not { IsInsideTree: true } held)
        {
            _heldContainer = null;
            return;
        }

        // Out of its cell the arrow stops, and starts again on coming back, as a browser's does.
        if (!InCell(held, _heldHorizontal, _heldEnd, mousePosition))
            return;

        _heldTime += deltaSeconds;
        while (_heldTime >= _nextRepeat)
        {
            Scroll(held, _heldHorizontal, _heldEnd);
            _nextRepeat += KsTguiScrollBarGeometry.RepeatInterval;
        }
    }

    /// <summary>
    ///     An array rather than a span literal: that compiles to a pointer into static data, which the sandbox's ILVerify refuses.
    /// </summary>
    private static readonly bool[] Both = [false, true];

    private static bool FindArrow(Control? hovered, Vector2 mousePosition, out ScrollContainer container, out bool horizontal, out bool end)
    {
        // Innermost first: a scroll container inside another covers the outer one's arrows where they overlap.
        for (var current = hovered; current != null; current = current.Parent)
        {
            if (current is not ScrollContainer scroll)
                continue;

            foreach (var candidateHorizontal in Both)
            {
                foreach (var candidateEnd in Both)
                {
                    if (!InCell(scroll, candidateHorizontal, candidateEnd, mousePosition))
                        continue;

                    container = scroll;
                    horizontal = candidateHorizontal;
                    end = candidateEnd;
                    return true;
                }
            }
        }

        container = null!;
        horizontal = false;
        end = false;
        return false;
    }

    /// <summary>
    ///     Whether the point is in the arrow's cell of this container's bar, which has to be showing and tgui's.
    /// </summary>
    public static bool InCell(ScrollContainer scroll, bool horizontal, bool end, Vector2 point)
    {
        var bar = FindBar(scroll, horizontal);
        if (bar == null || !bar.VisibleInTree)
            return false;

        if (!bar.TryGetStyleProperty<Robust.Client.Graphics.StyleBox>(ScrollBar.StylePropertyTrack, out var track)
            || track is not KsTguiScrollTrackStyleBox)
        {
            return false;
        }

        var barBox = UIBox2.FromDimensions(bar.GlobalPosition, bar.Size);
        return KsTguiScrollBarGeometry.Cell(barBox, horizontal, end, scale: 1f).Contains(point);
    }

    private static ScrollBar? FindBar(ScrollContainer scroll, bool horizontal)
    {
        foreach (var child in scroll.Children)
        {
            if (horizontal ? child is HScrollBar : child is VScrollBar)
                return (ScrollBar)child;
        }

        return null;
    }

    private static void Scroll(ScrollContainer scroll, bool horizontal, bool end)
    {
        var step = end ? KsTguiScrollBarGeometry.ArrowStep : -KsTguiScrollBarGeometry.ArrowStep;
        if (horizontal)
            scroll.HScrollTarget += step;
        else
            scroll.VScrollTarget += step;
    }
}
