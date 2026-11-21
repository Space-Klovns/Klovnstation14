using System.Numerics;
using Content.Shared._KS14.Mortar;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._KS14.Mortar;

/// <summary>
///     The mortar's aiming plot, north up: the mortar at the centre, its band of range between two rings, the aim with the
///         ring its shells may land in, and where its last shells did land, the newest strongest. Clicking or dragging in it
///         aims at that point, kept to the band, and the mouse wheel moves the aim in and out.
/// </summary>
/// <remarks>
///     It shows what it is given and says what the player picked: it never changes its own aim, only the window does
///         (<see cref="SetAim"/>), so that the window's fields and the plot are always the one aim.
/// </remarks>
public sealed partial class KsMortarAimControl : Control
{
    [Dependency] private IInputManager _inputManager = default!;

    /// <summary>
    ///     Room between the outer ring and the edge, in virtual pixels, for the compass marks.
    /// </summary>
    public const float RingMargin = 16f;

    /// <summary>
    ///     Tiles per notch of the mouse wheel, and with Shift held.
    /// </summary>
    public const float WheelStep = 1f;

    public const float WheelShiftStep = 5f;

    private const int Segments = 72;

    private static readonly Color BandColor = Color.FromHex("#C8A040").WithAlpha(0.16f);
    private static readonly Color RingColor = Color.FromHex("#C8A040").WithAlpha(0.8f);
    private static readonly Color GridColor = Color.White.WithAlpha(0.15f);
    private static readonly Color MarkColor = Color.FromHex("#C0C0C0");
    private static readonly Color MortarColor = Color.White;
    private static readonly Color ImpactColor = Color.FromHex("#FF6A3D");
    private static readonly Color AimColor = Color.FromHex("#6CFF6C");
    private static readonly Color InvalidAimColor = Color.FromHex("#808080");

    private readonly string[] _marks = new string[4];
    private readonly Vector2[] _bandVertices = new Vector2[(Segments + 1) * 2];
    private readonly List<Vector2> _impacts = new();
    private bool _dragging;

    public float MinRange { get; private set; }

    public float MaxRange { get; private set; } = 1f;

    /// <summary>
    ///     How far from the aim a shell may land, in tiles.
    /// </summary>
    public float Spread { get; set; }

    public Angle Bearing { get; private set; }

    public float Distance { get; private set; }

    /// <summary>
    ///     Whether the aim shown is one that can be fired at; drawn greyed out when not.
    /// </summary>
    public bool AimValid { get; set; } = true;

    public IReadOnlyList<Vector2> Impacts => _impacts;

    /// <summary>
    ///     The player picked an aim on the plot: a compass bearing in whole degrees and a distance in whole tiles, kept to
    ///         the band.
    /// </summary>
    public event Action<Angle, float>? OnAimPicked;

    public KsMortarAimControl()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;

        _marks[0] = Loc.GetString("ks-mortar-compass-0");
        _marks[1] = Loc.GetString("ks-mortar-compass-2");
        _marks[2] = Loc.GetString("ks-mortar-compass-4");
        _marks[3] = Loc.GetString("ks-mortar-compass-6");
    }

    public void SetRange(float minRange, float maxRange)
    {
        if (!float.IsFinite(minRange) || !float.IsFinite(maxRange))
            return;

        MaxRange = MathF.Max(maxRange, 0.01f);
        MinRange = Math.Clamp(minRange, 0f, MaxRange);
    }

    public void SetAim(Angle bearing, float distance)
    {
        Bearing = bearing;
        Distance = distance;
    }

    public void SetImpacts(IReadOnlyList<Vector2> impacts)
    {
        _impacts.Clear();
        foreach (var impact in impacts)
        {
            if (float.IsFinite(impact.X) && float.IsFinite(impact.Y))
                _impacts.Add(impact);
        }
    }

    /// <summary>
    ///     The radius of the outer ring, in virtual pixels.
    /// </summary>
    public float RingRadius => MathF.Max(0f, MathF.Min(Size.X, Size.Y) / 2f - RingMargin);

    /// <summary>
    ///     Where on the plot, in virtual pixels from its top left, an offset from the mortar (map axes, +Y north) is.
    /// </summary>
    public Vector2 OffsetToPoint(Vector2 offset)
    {
        var scale = RingRadius / MaxRange;
        return Size / 2f + new Vector2(offset.X, -offset.Y) * scale;
    }

    /// <summary>
    ///     Where on the plot, in virtual pixels from its top left, an aim is.
    /// </summary>
    public Vector2 AimToPoint(Angle bearing, float distance)
    {
        return OffsetToPoint(KsMortarAim.ToOffset(bearing, distance));
    }

    /// <summary>
    ///     The aim at a point on the plot, in virtual pixels from its top left: its compass bearing in whole degrees, and its
    ///         distance in whole tiles kept to the band.
    /// </summary>
    public (Angle Bearing, float Distance) PointToAim(Vector2 point)
    {
        var radius = RingRadius;
        var relative = point - Size / 2f;
        var offset = radius > 0f ? new Vector2(relative.X, -relative.Y) * (MaxRange / radius) : Vector2.Zero;

        var degrees = KsMortarAim.NormalizeDegrees(Math.Round(KsMortarAim.ToBearing(offset).Degrees));
        var distance = Math.Clamp(MathF.Round(offset.Length()), MinRange, MaxRange);
        return (Angle.FromDegrees(degrees), distance);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _dragging = true;
        Pick(args.RelativePosition);
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function == EngineKeyFunctions.UIClick)
            _dragging = false;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_dragging)
            Pick(args.RelativePosition);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _dragging = false;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (args.Delta.Y == 0f)
            return;

        var step = _inputManager.IsKeyDown(Keyboard.Key.Shift) ? WheelShiftStep : WheelStep;
        var distance = Math.Clamp(Distance + MathF.Sign(args.Delta.Y) * step, MinRange, MaxRange);
        OnAimPicked?.Invoke(Bearing, distance);
        args.Handle();
    }

    private void Pick(Vector2 point)
    {
        var (bearing, distance) = PointToAim(point);
        OnAimPicked?.Invoke(bearing, distance);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var centre = (Vector2) PixelSize / 2f;
        var radius = RingRadius * UIScale;
        if (radius < 8f)
            return;

        var scale = radius / MaxRange;
        var innerRadius = MinRange * scale;

        // The band it can reach, shaded, between its two rings.
        for (var i = 0; i <= Segments; i++)
        {
            var theta = MathF.Tau * i / Segments;
            var direction = new Vector2(MathF.Sin(theta), -MathF.Cos(theta));
            _bandVertices[i * 2] = centre + direction * innerRadius;
            _bandVertices[i * 2 + 1] = centre + direction * radius;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _bandVertices, BandColor);
        handle.DrawCircle(centre, radius, RingColor, filled: false);
        if (innerRadius > 1f)
            handle.DrawCircle(centre, innerRadius, RingColor, filled: false);

        handle.DrawLine(centre - new Vector2(0f, radius), centre + new Vector2(0f, radius), GridColor);
        handle.DrawLine(centre - new Vector2(radius, 0f), centre + new Vector2(radius, 0f), GridColor);
        DrawMarks(handle, centre, radius);

        // Fall of shot: the oldest faintest.
        for (var i = 0; i < _impacts.Count; i++)
        {
            var strength = (i + 1f) / _impacts.Count;
            var point = centre + new Vector2(_impacts[i].X, -_impacts[i].Y) * scale;
            handle.DrawCircle(point, 2.5f * UIScale, ImpactColor.WithAlpha(0.2f + 0.8f * strength));
        }

        handle.DrawRect(UIBox2.FromDimensions(centre - new Vector2(2f, 2f) * UIScale, new Vector2(4f, 4f) * UIScale), MortarColor);

        if (!float.IsFinite(Distance) || !double.IsFinite(Bearing.Theta))
            return;

        var aimColor = AimValid ? AimColor : InvalidAimColor;
        var offset = KsMortarAim.ToOffset(Bearing, Distance);
        var aim = centre + new Vector2(offset.X, -offset.Y) * scale;
        var arm = 5f * UIScale;

        handle.DrawLine(centre, aim, aimColor.WithAlpha(0.35f));
        handle.DrawLine(aim - new Vector2(arm, 0f), aim + new Vector2(arm, 0f), aimColor);
        handle.DrawLine(aim - new Vector2(0f, arm), aim + new Vector2(0f, arm), aimColor);
        handle.DrawCircle(aim, MathF.Max(Spread * scale, 2f * UIScale), aimColor.WithAlpha(0.8f), filled: false);
    }

    private void DrawMarks(DrawingHandleScreen handle, Vector2 centre, float radius)
    {
        var font = UserInterfaceManager.ThemeDefaults.DefaultFont;
        var textScale = UIScale;

        for (var i = 0; i < _marks.Length; i++)
        {
            var theta = MathF.PI / 2f * i;
            var direction = new Vector2(MathF.Sin(theta), -MathF.Cos(theta));
            var dimensions = handle.GetDimensions(font, _marks[i], textScale);

            // Just outside the ring, centred on its direction.
            var labelCentre = centre + direction * (radius + 2f * UIScale + MathF.Max(dimensions.X, dimensions.Y) / 2f);
            var position = Vector2.Clamp(labelCentre - dimensions / 2f, Vector2.Zero, Vector2.Max(Vector2.Zero, (Vector2) PixelSize - dimensions));
            handle.DrawString(font, position, _marks[i], textScale, MarkColor);
        }
    }
}
