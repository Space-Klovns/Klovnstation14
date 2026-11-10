using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     A Font Awesome glyph, as tgui's Icon. The font is loaded from the bundled file whatever the theme, so it also
///         shows under Baron.
/// </summary>
public sealed partial class KsTguiIcon : Label
{
    private const float BaseSize = 12f;

    /// <summary>
    ///     Font Awesome's <c>fa-spin</c>: one turn every two seconds, linear.
    /// </summary>
    public const float SpinSecondsPerTurn = 2f;

    [Dependency] private KsTguiFonts _fonts = default!;
    [Dependency] private IGameTiming _timing = default!;

    private string? _glyph;
    private float _scale = 1f;
    private bool _regular;
    private float _rotation;
    private bool _spin;

    public KsTguiIcon()
    {
        IoCManager.InjectDependencies(this);
        RefreshFont();
    }

    /// <summary>
    ///     The glyph to show, normally a <see cref="KsTguiIcons"/> constant.
    /// </summary>
    public string? Glyph
    {
        get => _glyph;
        set
        {
            _glyph = value;
            Text = value;
        }
    }

    /// <summary>
    ///     Size in ems, as tgui's <c>size</c>: 1 is 12 pixels.
    /// </summary>
    public float IconSize
    {
        get => _scale;
        set
        {
            _scale = value;
            RefreshFont();
        }
    }

    /// <summary>
    ///     tgui's <c>-o</c> suffix: the Font Awesome Regular face instead of Solid.
    /// </summary>
    public bool Regular
    {
        get => _regular;
        set
        {
            if (_regular == value)
                return;

            _regular = value;
            RefreshFont();
        }
    }

    /// <summary>
    ///     tgui's <c>rotation</c>, in degrees clockwise.
    /// </summary>
    public float Rotation
    {
        get => _rotation;
        set => _rotation = value;
    }

    /// <summary>
    ///     tgui's <c>spin</c>.
    /// </summary>
    public bool Spin
    {
        get => _spin;
        set => _spin = value;
    }

    /// <summary>
    ///     The glyph's colour; null follows the surrounding text.
    /// </summary>
    public Color? IconColor
    {
        get => FontColorOverride;
        set => FontColorOverride = value;
    }

    /// <summary>
    ///     The angle the glyph is drawn at now, in degrees: <see cref="Rotation"/>, plus the spin if it is on.
    /// </summary>
    public float CurrentAngle
    {
        get
        {
            if (!_spin)
                return _rotation;

            var turns = (float)(_timing.RealTime.TotalSeconds / SpinSecondsPerTurn % 1.0);
            return _rotation + turns * 360f;
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var angle = CurrentAngle;
        if (angle % 360f == 0f)
        {
            base.Draw(handle);
            return;
        }

        // CSS rotates around the middle of the element.
        var previous = handle.GetTransform();
        var middle = (Vector2)PixelSize / 2f;
        handle.SetTransform(Matrix3x2.CreateRotation(MathHelper.DegreesToRadians(angle), middle) * previous);
        base.Draw(handle);
        handle.SetTransform(previous);
    }

    private void RefreshFont()
    {
        FontOverride = _fonts.GetIconFont(Math.Max(1, (int)MathF.Round(BaseSize * _scale)), _regular);
    }
}
