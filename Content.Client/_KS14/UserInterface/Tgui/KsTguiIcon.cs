using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     The Font Awesome icons the ported tgui windows use. There is no icon font in the game, so each is drawn.
/// </summary>
public enum KsTguiIconKind : byte
{
    None,
    Eye,
    Times,
    PowerOff,
    Plus,
    Eject,
    FastForward,
    FastBackward,
    Undo,
    Sync,
    Square,
    CheckSquare,
    Tint,
    Plug,
    Flask,
    Exchange,
    Trash,
    Sort,
    Faucet,
}

/// <summary>
///     A single tgui icon, drawn from primitives and centred in the control. It's square: the shorter side sets its size.
/// </summary>
public sealed class KsTguiIcon : Control
{
    /// <summary>
    ///     Stroke width, as a fraction of the icon's size.
    /// </summary>
    private const float Stroke = 0.14f;

    private readonly KsTguiPrimitives _primitives = new();
    private KsTguiIconKind _builtKind = KsTguiIconKind.None;
    private Vector2i _builtForSize;

    public KsTguiIcon()
    {
        MouseFilter = MouseFilterMode.Ignore;
        MinSize = new Vector2(12f, 12f);
    }

    public KsTguiIconKind Kind { get; set; }

    public Color Color { get; set; } = KsTguiTheme.Text;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (Kind == KsTguiIconKind.None)
            return;

        // The shape only changes with the kind or the size.
        if (_builtKind != Kind || _builtForSize != PixelSize)
        {
            _builtKind = Kind;
            _builtForSize = PixelSize;

            var size = MathF.Min(PixelSize.X, PixelSize.Y);
            _primitives.Begin((PixelSize - new Vector2(size)) / 2f, size);
            Build(_primitives, Kind);
        }

        _primitives.Draw(handle, Color);
    }

    /// <summary>
    ///     Adds the icon's shapes, in a 0..1 box with y pointing down.
    /// </summary>
    private static void Build(KsTguiPrimitives shapes, KsTguiIconKind kind)
    {
        var center = new Vector2(0.5f, 0.5f);

        switch (kind)
        {
            case KsTguiIconKind.Eye:
                // Two lids meeting at the corners, around a pupil.
                shapes.Arc(new Vector2(0.5f, 0.95f), 0.6f, 229f, 311f, Stroke);
                shapes.Arc(new Vector2(0.5f, 0.05f), 0.6f, 49f, 131f, Stroke);
                shapes.Disc(center, 0.14f);
                break;
            case KsTguiIconKind.Times:
                shapes.Line(new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f), Stroke * 1.2f);
                shapes.Line(new Vector2(0.8f, 0.2f), new Vector2(0.2f, 0.8f), Stroke * 1.2f);
                break;
            case KsTguiIconKind.PowerOff:
                shapes.Arc(new Vector2(0.5f, 0.55f), 0.34f, -50f, 230f, Stroke);
                shapes.Line(new Vector2(0.5f, 0.06f), new Vector2(0.5f, 0.5f), Stroke);
                break;
            case KsTguiIconKind.Plus:
                shapes.Rect(0.1f, 0.5f - Stroke * 0.6f, 0.9f, 0.5f + Stroke * 0.6f);
                shapes.Rect(0.5f - Stroke * 0.6f, 0.1f, 0.5f + Stroke * 0.6f, 0.9f);
                break;
            case KsTguiIconKind.Eject:
                shapes.Triangle(new Vector2(0.5f, 0.12f), new Vector2(0.9f, 0.58f), new Vector2(0.1f, 0.58f));
                shapes.Rect(0.1f, 0.7f, 0.9f, 0.86f);
                break;
            case KsTguiIconKind.FastForward:
                shapes.Triangle(new Vector2(0.08f, 0.2f), new Vector2(0.5f, 0.5f), new Vector2(0.08f, 0.8f));
                shapes.Triangle(new Vector2(0.5f, 0.2f), new Vector2(0.92f, 0.5f), new Vector2(0.5f, 0.8f));
                break;
            case KsTguiIconKind.FastBackward:
                shapes.Triangle(new Vector2(0.92f, 0.2f), new Vector2(0.92f, 0.8f), new Vector2(0.5f, 0.5f));
                shapes.Triangle(new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.8f), new Vector2(0.08f, 0.5f));
                break;
            case KsTguiIconKind.Undo:
                // Counter-clockwise from the lower right, over the top, ending in a head pointing down at the left.
                shapes.Arc(new Vector2(0.55f, 0.52f), 0.32f, 180f, 420f, Stroke);
                shapes.ArrowHead(new Vector2(0.55f, 0.52f), 0.32f, 180f, 0.22f, clockwise: false);
                break;
            case KsTguiIconKind.Sync:
                shapes.Arc(center, 0.33f, 200f, 330f, Stroke);
                shapes.Arc(center, 0.33f, 20f, 150f, Stroke);
                shapes.ArrowHead(center, 0.33f, 330f, 0.2f, clockwise: true);
                shapes.ArrowHead(center, 0.33f, 150f, 0.2f, clockwise: true);
                break;
            case KsTguiIconKind.Square:
                shapes.RectOutline(0.14f, 0.14f, 0.86f, 0.86f, Stroke);
                break;
            case KsTguiIconKind.CheckSquare:
                shapes.RectOutline(0.14f, 0.14f, 0.86f, 0.86f, Stroke);
                shapes.Line(new Vector2(0.28f, 0.5f), new Vector2(0.45f, 0.67f), Stroke);
                shapes.Line(new Vector2(0.42f, 0.67f), new Vector2(0.74f, 0.34f), Stroke);
                break;
            case KsTguiIconKind.Tint:
                shapes.Disc(new Vector2(0.5f, 0.63f), 0.27f);
                shapes.Triangle(new Vector2(0.5f, 0.06f), new Vector2(0.75f, 0.53f), new Vector2(0.25f, 0.53f));
                break;
            case KsTguiIconKind.Plug:
                shapes.Rect(0.3f, 0.06f, 0.4f, 0.32f);
                shapes.Rect(0.6f, 0.06f, 0.7f, 0.32f);
                shapes.Rect(0.2f, 0.3f, 0.8f, 0.5f);
                shapes.Triangle(new Vector2(0.2f, 0.5f), new Vector2(0.8f, 0.5f), new Vector2(0.5f, 0.76f));
                shapes.Rect(0.44f, 0.66f, 0.56f, 0.95f);
                break;
            case KsTguiIconKind.Flask:
                shapes.Rect(0.4f, 0.06f, 0.6f, 0.4f);
                shapes.Rect(0.32f, 0.06f, 0.68f, 0.14f);
                shapes.Quad(new Vector2(0.4f, 0.38f), new Vector2(0.6f, 0.38f), new Vector2(0.88f, 0.92f), new Vector2(0.12f, 0.92f));
                break;
            case KsTguiIconKind.Exchange:
                shapes.Rect(0.1f, 0.3f, 0.72f, 0.3f + Stroke);
                shapes.Triangle(new Vector2(0.68f, 0.14f), new Vector2(0.94f, 0.3f + Stroke / 2f), new Vector2(0.68f, 0.46f + Stroke));
                shapes.Rect(0.28f, 0.7f - Stroke, 0.9f, 0.7f);
                shapes.Triangle(new Vector2(0.32f, 0.54f - Stroke), new Vector2(0.32f, 0.86f), new Vector2(0.06f, 0.7f - Stroke / 2f));
                break;
            case KsTguiIconKind.Trash:
                shapes.Rect(0.12f, 0.16f, 0.88f, 0.27f);
                shapes.Rect(0.38f, 0.06f, 0.62f, 0.16f);
                shapes.Quad(new Vector2(0.2f, 0.32f), new Vector2(0.8f, 0.32f), new Vector2(0.74f, 0.94f), new Vector2(0.26f, 0.94f));
                break;
            case KsTguiIconKind.Sort:
                shapes.Triangle(new Vector2(0.5f, 0.04f), new Vector2(0.82f, 0.42f), new Vector2(0.18f, 0.42f));
                shapes.Triangle(new Vector2(0.18f, 0.58f), new Vector2(0.82f, 0.58f), new Vector2(0.5f, 0.96f));
                break;
            case KsTguiIconKind.Faucet:
                shapes.Rect(0.08f, 0.3f, 0.7f, 0.46f);
                shapes.Rect(0.54f, 0.3f, 0.7f, 0.66f);
                shapes.Rect(0.3f, 0.12f, 0.46f, 0.3f);
                shapes.Rect(0.2f, 0.06f, 0.56f, 0.14f);
                shapes.Disc(new Vector2(0.62f, 0.86f), 0.09f);
                break;
        }
    }
}
