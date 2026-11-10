using System.Globalization;
using System.Numerics;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Input;

namespace Content.Client._KS14.Tgui.Chat;

/// <summary>
///     The floating window tgui-say lives in when the chat has no docked bar: a <see cref="KsTguiSayBar"/> with a drag zone at
///         its right edge. It sits in the game screen's layout container, shown by the chat-focus keybinds and hidden when
///         the message is sent or escaped. Where it was dragged to is kept.
/// </summary>
public sealed partial class KsTguiSayWindow : Control
{
    /// <summary>
    ///     Width of the drag zone, which is part of the window's <see cref="KsTguiSayBar.WindowWidth"/>.
    /// </summary>
    public const float GripWidth = 14f;

    /// <summary>
    ///     Where it first opens, as a fraction of the screen: low and central, above the hotbar.
    /// </summary>
    private static readonly Vector2 DefaultPosition = new(0.5f, 0.74f);

    [Dependency] private IConfigurationManager _configurationManager = default!;

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
    ///     Opens it at its remembered place, or at the default.
    /// </summary>
    public void Open()
    {
        if (Parent == null)
            return;

        Visible = true;
        MoveTo(RememberedPosition());
    }

    public void Close()
    {
        Visible = false;
    }

    private Vector2 RememberedPosition()
    {
        var fraction = DefaultPosition;
        var parts = _configurationManager.GetCVar(KsCCVars.TguiSayPosition).Split(',');
        if (parts.Length == 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && float.IsFinite(x) && float.IsFinite(y))
        {
            fraction = new Vector2(x, y);
        }

        var size = Parent!.Size;
        return new Vector2(fraction.X * size.X - KsTguiSayBar.WindowWidth / 2f, fraction.Y * size.Y);
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

    private void RememberPosition()
    {
        if (Parent == null || Parent.Size.X <= 0f || Parent.Size.Y <= 0f)
            return;

        var centre = new Vector2(Position.X + KsTguiSayBar.WindowWidth / 2f, Position.Y);
        // TODO SANDBOX: string.Create(CultureInfo.InvariantCulture, $"...") would say this in one line, but isn't whitelisted.
        var fractionX = (centre.X / Parent.Size.X).ToString("0.####", CultureInfo.InvariantCulture);
        var fractionY = (centre.Y / Parent.Size.Y).ToString("0.####", CultureInfo.InvariantCulture);
        var text = fractionX + "," + fractionY;
        _configurationManager.SetCVar(KsCCVars.TguiSayPosition, text);
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
            _window.RememberPosition();
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
