using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     tgui's RoundGauge: a half-ring split into good, average and bad bands with a needle at the value, the value
///         written underneath, and a blinking alert once the value passes <see cref="AlertAfter"/>.
/// </summary>
public sealed partial class KsTguiRoundGauge : BoxContainer
{
    [Dependency] private IResourceCache _resourceCache = default!;

    private readonly Dial _dial;
    private readonly Label _label;

    public KsTguiRoundGauge()
    {
        IoCManager.InjectDependencies(this);

        Orientation = LayoutOrientation.Vertical;
        HorizontalAlignment = HAlignment.Center;

        _dial = new Dial(this) { HorizontalAlignment = HAlignment.Center };
        _label = new Label
        {
            Align = Label.AlignMode.Center,
            FontOverride = KsTguiTheme.RegularFont(_resourceCache),
            FontColorOverride = KsTguiTheme.Text,
        };

        AddChild(_dial);
        AddChild(_label);
        DialSize = 1.75f;
    }

    public float MinValue { get; set; }

    public float MaxValue { get; set; } = 1f;

    public float Value { get; set; }

    /// <summary>
    ///     Where the average band starts. Below it is good.
    /// </summary>
    public float AverageFrom { get; set; } = 0.7f;

    /// <summary>
    ///     Where the bad band starts. It runs to <see cref="MaxValue"/>.
    /// </summary>
    public float BadFrom { get; set; } = 0.85f;

    /// <summary>
    ///     Above this the alert light blinks. Null for none.
    /// </summary>
    public float? AlertAfter { get; set; }

    /// <summary>
    ///     The value as text, shown under the dial.
    /// </summary>
    public string? Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    /// <summary>
    ///     tgui's <c>size</c>, in ems: the dial is 2.6 by 1.3 of them.
    /// </summary>
    public float DialSize
    {
        get => _dial.SetSize.X / (2.6f * KsTguiTheme.FontSize);
        set => _dial.SetSize = new Vector2(2.6f, 1.3f) * value * KsTguiTheme.FontSize;
    }

    private float Fraction(float value)
    {
        return MaxValue > MinValue ? Math.Clamp((value - MinValue) / (MaxValue - MinValue), 0f, 1f) : 0f;
    }

    private sealed partial class Dial : Control
    {
        private static readonly Color Track = Color.White.WithAlpha(0.1f);
        private static readonly Color Needle = Color.FromHex("#E6E6E6");

        [Dependency] private IGameTiming _timing = default!;

        private readonly KsTguiRoundGauge _gauge;
        private readonly KsTguiPrimitives _shapes = new();

        public Dial(KsTguiRoundGauge gauge)
        {
            IoCManager.InjectDependencies(this);
            _gauge = gauge;
            MouseFilter = MouseFilterMode.Ignore;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            var outer = MathF.Min(PixelSize.X / 2f, PixelSize.Y);
            if (outer <= 0f)
                return;

            var width = outer * 0.22f;
            var radius = outer - width / 2f;
            var center = new Vector2(PixelSize.X / 2f, PixelSize.Y);

            _shapes.Begin(Vector2.Zero, 1f);
            _shapes.Arc(center, radius, 180f, 360f, width, segments: 24);
            _shapes.Draw(handle, Track);

            var averageFrom = _gauge.Fraction(_gauge.AverageFrom);
            var badFrom = _gauge.Fraction(_gauge.BadFrom);
            DrawBand(handle, center, radius, width * 0.7f, 0f, averageFrom, KsTguiTheme.Good);
            DrawBand(handle, center, radius, width * 0.7f, averageFrom, badFrom, KsTguiTheme.Average);
            DrawBand(handle, center, radius, width * 0.7f, badFrom, 1f, KsTguiTheme.Bad);

            var angle = MathHelper.DegreesToRadians(180f + 180f * _gauge.Fraction(_gauge.Value));
            var tip = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (radius + width / 2f);
            _shapes.Begin(Vector2.Zero, 1f);
            _shapes.Line(center, tip, MathF.Max(2f, outer * 0.07f));
            _shapes.Disc(center, outer * 0.13f);
            _shapes.Draw(handle, Needle);

            // tgui blinks the alert at about 1Hz while over the threshold.
            if (_gauge.AlertAfter is { } alertAfter && _gauge.Value > alertAfter &&
                _timing.RealTime.TotalSeconds % 1.0 < 0.5)
            {
                handle.DrawCircle(new Vector2(width * 0.6f, width * 0.6f), width * 0.6f, KsTguiTheme.Bad);
            }
        }

        private void DrawBand(DrawingHandleScreen handle, Vector2 center, float radius, float width, float from, float to, Color color)
        {
            if (to <= from)
                return;

            _shapes.Begin(Vector2.Zero, 1f);
            _shapes.Arc(center, radius, 180f + 180f * from, 180f + 180f * to, width, segments: Math.Max(2, (int)MathF.Ceiling(24 * (to - from))));
            _shapes.Draw(handle, color);
        }
    }
}
