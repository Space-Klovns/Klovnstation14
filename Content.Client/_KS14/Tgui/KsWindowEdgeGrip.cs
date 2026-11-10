using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     Lets a window be resized from its edges when its contents run edge to edge. <c>BaseWindow</c> only works out a drag when
///         a press lands on the window itself, so contents that stop clicks right up to the border swallow them.
/// </summary>
/// <remarks>
///     Covers the window as its topmost child, but only answers the hit test in the resize zones, and lets what it
///         receives pass: the press and the movement bubble to the window, which does the dragging as it always has, through
///         its own <c>GetDragModeFor</c>. Only the cursor is its own, since the engine reads that from the hovered control and
///         not its parents.
/// </remarks>
public sealed class KsWindowEdgeGrip : Control
{
    private readonly Func<Vector2, bool> _isResizeZone;
    private readonly Func<Vector2, CursorShape> _cursorAt;

    public KsWindowEdgeGrip(Func<Vector2, bool> isResizeZone, Func<Vector2, CursorShape> cursorAt)
    {
        _isResizeZone = isResizeZone;
        _cursorAt = cursorAt;
        MouseFilter = MouseFilterMode.Pass;
    }

    protected override bool HasPoint(Vector2 point)
    {
        return base.HasPoint(point) && _isResizeZone(point);
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        // A drag keeps the shape it started with.
        if (UserInterfaceManager.CurrentlyHovered == this)
            DefaultCursorShape = _cursorAt(args.RelativePosition);
    }

    protected override void MouseExited()
    {
        base.MouseExited();

        DefaultCursorShape = CursorShape.Arrow;
    }
}
