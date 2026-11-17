using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._KS14.Tgui.Chat;

/// <summary>
///     The floating window tgui-say lives in when the chat has no docked bar: a <see cref="KsTguiSayBar"/> with a drag zone at
///         its right edge. It sits in the game screen's layout container, shown by the chat-focus keybinds just under the
///         mouse pointer, where the player is looking, and hidden when the message is sent or escaped.
/// </summary>
public sealed partial class KsTguiSayWindow : Control
{
    /// <summary>
    ///     Width of the drag zone, which is part of the window's <see cref="KsTguiSayBar.WindowWidth"/>.
    /// </summary>
    public const float GripWidth = 14f;

    /// <summary>
    ///     How far under the pointer its top edge opens, so the pointer doesn't cover the text.
    /// </summary>
    public const float PointerGap = 16f;

    [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;

    private readonly BoxContainer _row;
    private readonly Grip _grip;

    public KsTguiSayWindow()
    {
        IoCManager.InjectDependencies(this);

        Visible = false;
        MouseFilter = MouseFilterMode.Pass;
        SetWidth = KsTguiSayBar.WindowWidth;

        _row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 0 };
        _grip = new Grip(this) { MinWidth = GripWidth };
        _row.AddChild(_grip);
        AddChild(_row);
    }

    /// <summary>
    ///     Puts the bar in the window, in front of the drag zone.
    /// </summary>
    public void Host(KsTguiSayBar bar)
    {
        bar.Orphan();
        bar.HorizontalExpand = true;
        _row.AddChild(bar);
        bar.SetPositionInParent(0);
    }

    /// <summary>
    ///     Opens it just under the mouse pointer.
    /// </summary>
    public void Open()
    {
        OpenUnder(_userInterfaceManager.MousePositionScaled.Position);
    }

    /// <summary>
    ///     Opens it centred under a point of the screen, in UI units, kept on screen.
    /// </summary>
    public void OpenUnder(Vector2 pointer)
    {
        if (Parent == null)
            return;

        Visible = true;
        var local = pointer - Parent.GlobalPosition;
        MoveTo(new Vector2(local.X - KsTguiSayBar.WindowWidth / 2f, local.Y + PointerGap));
    }

    public void Close()
    {
        Visible = false;
    }

    /// <summary>
    ///     Moves its top left to a point of its parent, keeping all of it on screen.
    /// </summary>
    public void MoveTo(Vector2 position)
    {
        if (Parent == null)
            return;

        var parentSize = Parent.Size;
        var maxX = MathF.Max(0f, parentSize.X - KsTguiSayBar.WindowWidth);
        var maxY = MathF.Max(0f, parentSize.Y - KsTguiSayBar.LargeHeight);
        position = new Vector2(Math.Clamp(position.X, 0f, maxX), Math.Clamp(position.Y, 0f, maxY));

        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.TopLeft);
        LayoutContainer.SetPosition(this, position);
    }

    /// <summary>
    ///     The strip at the right edge that drags the window.
    /// </summary>
    private sealed class Grip : Control
    {
        private static readonly Color Dots = KsTguiPalettes.TextDim;

        private readonly KsTguiSayWindow _window;
        private Vector2? _grabOffset;

        public Grip(KsTguiSayWindow window)
        {
            _window = window;
            MouseFilter = MouseFilterMode.Stop;
            DefaultCursorShape = CursorShape.Crosshair;
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            _grabOffset = args.PointerLocation.Position / UIScale - _window.Position;
            args.Handle();
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            base.KeyBindUp(args);
            if (args.Function != EngineKeyFunctions.UIClick || _grabOffset == null)
                return;

            _grabOffset = null;
            args.Handle();
        }

        protected override void MouseMove(GUIMouseMoveEventArgs args)
        {
            base.MouseMove(args);
            if (_grabOffset is { } offset)
                _window.MoveTo(args.GlobalPosition - offset);
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            // Two columns of three dots, tgui's idea of a handle.
            var scale = UIScale;
            var centre = PixelSize / 2;
            for (var column = -1; column <= 1; column += 2)
            {
                for (var row = -1; row <= 1; row++)
                {
                    var origin = new Vector2(centre.X + column * 2.5f * scale, centre.Y + row * 5f * scale);
                    handle.DrawRect(UIBox2.FromDimensions(origin - new Vector2(scale), new Vector2(2f * scale)), Dots);
                }
            }
        }
    }
}
