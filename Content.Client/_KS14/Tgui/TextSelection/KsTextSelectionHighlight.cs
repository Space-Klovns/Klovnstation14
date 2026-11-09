using System.Collections.Generic;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._KS14.Tgui.TextSelection;

/// <summary>
///     Draws the selection over the screen. It sits in the popup layer with <see cref="Control.MouseFilterMode.Ignore"/>, so it
///         takes no input, and covers the whole screen so that its pixels are the screen's. The rectangles it draws are
///         already clipped to the control that holds the text; <see cref="KsTguiTextSelectionUIController"/> works them out.
/// </summary>
public sealed class KsTextSelectionHighlight : Control
{
    /// <summary>
    ///     The browser's default selection colour, <c>#3367D1</c> at half alpha.
    /// </summary>
    public static readonly Color SelectionColor = new(0x33 / 255f, 0x67 / 255f, 0xD1 / 255f, 0.5f);

    /// <summary>
    ///     The rectangles to draw, in screen pixels. Empty while there is no selection.
    /// </summary>
    public readonly List<UIBox2> Rectangles = new();

    public KsTextSelectionHighlight()
    {
        MouseFilter = MouseFilterMode.Ignore;
        Visible = false;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        foreach (var rectangle in Rectangles)
            handle.DrawRect(rectangle, SelectionColor);
    }
}
