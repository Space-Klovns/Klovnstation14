using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     Gives a <see cref="DefaultWindow"/> tgui's drag behaviour. Its drag rules are the engine's and can't be overridden from
///         out here: moves in a fixed 25px, resizes from all four sides. So this sits over the window's empty space, takes
///         the presses that would've reached those rules, and does the moving itself.
/// </summary>
/// <remarks>
///     Goes beneath the header row and contents, so that anything they hold is hit first, and a press lands here only on
///         their empty space. It stops the event, so the window never sees it: it brings the window to the front itself.
/// </remarks>
public sealed class KsTguiWindowHandle : Control
{
    /// <summary>
    ///     The engine's resize margin. Presses here have to be stopped or they'd resize from the west.
    /// </summary>
    private const float EngineEdge = 7f;

    private readonly DefaultWindow _window;

    private KsTguiDragZone _drag = KsTguiDragZone.None;
    private Vector2 _offsetTopLeft;
    private Vector2 _offsetBottomRight;

    public KsTguiWindowHandle(DefaultWindow window)
    {
        _window = window;
        MouseFilter = MouseFilterMode.Stop;
    }

    protected override bool HasPoint(Vector2 point)
    {
        // The south and east handles stay with the engine. Its 7px are wider than tgui's, but contents start 6px in.
        return KsTguiWindowZones.Classify(point, Size, _window.Resizable) switch
        {
            KsTguiDragZone.Move or KsTguiDragZone.BottomRight => true,
            KsTguiDragZone.None => _window.Resizable && point.X < EngineEdge,
            _ => false,
        };
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _window.MoveToFront();

        _drag = KsTguiWindowZones.Classify(args.RelativePosition, Size, _window.Resizable);
        var pointer = args.PointerLocation.Position / UIScale;
        _offsetTopLeft = pointer - _window.Position;
        _offsetBottomRight = _window.Position + _window.Size - pointer;
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _drag = KsTguiDragZone.None;

        // As the window would, had the press reached it.
        UserInterfaceManager.KeyboardFocused?.ReleaseKeyboardFocus();
    }

    protected override void ControlFocusExited()
    {
        base.ControlFocusExited();

        _drag = KsTguiDragZone.None;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_window.Parent is not { } parent)
            return;

        switch (_drag)
        {
            case KsTguiDragZone.Move:
                var pointer = Vector2.Clamp(args.GlobalPosition, Vector2.Zero, parent.Size);
                LayoutContainer.SetPosition(_window, pointer - _offsetTopLeft);
                break;

            case KsTguiDragZone.BottomRight:
                var topLeft = _window.Position;
                var right = MathF.Max(args.GlobalPosition.X + _offsetBottomRight.X, topLeft.X + _window.MinSize.X);
                var bottom = MathF.Max(args.GlobalPosition.Y + _offsetBottomRight.Y, topLeft.Y + _window.MinSize.Y);
                _window.SetSize = new Vector2(right, bottom) - topLeft;
                break;

            default:
                var hoveringCorner = KsTguiWindowZones.Classify(args.RelativePosition, Size, _window.Resizable)
                    == KsTguiDragZone.BottomRight;
                DefaultCursorShape = hoveringCorner ? CursorShape.NWSEResize : CursorShape.Arrow;
                break;
        }
    }

    protected override void MouseExited()
    {
        base.MouseExited();

        if (_drag == KsTguiDragZone.None)
            DefaultCursorShape = CursorShape.Arrow;
    }
}
