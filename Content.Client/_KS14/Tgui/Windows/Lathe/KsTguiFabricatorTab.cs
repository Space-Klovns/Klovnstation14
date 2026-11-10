using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Windows.Lathe;

/// <summary>
///     A category in tg's fabricator browser (<c>.FabricatorTabs__Tab</c> in <c>Fabricator.scss</c>), which is its own look
///         rather than tgui's vertical Tabs: the name and its count in brackets pushed apart, a hover wash, and on the
///         selected one the wash, light text and a primary-coloured bar down the right.
/// </summary>
public sealed class KsTguiFabricatorTab : ContainerButton
{
    /// <summary>
    ///     <c>--transition-time-fast</c>, on the wash and the bar.
    /// </summary>
    private const float TransitionSeconds = 0.1f;

    /// <summary>
    ///     <c>--tab-indicator-size</c>.
    /// </summary>
    private const float IndicatorWidth = 2f;

    /// <summary>
    ///     <c>--tab-background-hover</c>: <c>--color-hover</c> at three quarters of its lightness.
    /// </summary>
    public static readonly Color Wash = KsTguiPalettes.Translucent(Color.FromHex("#606060"), 0.25f, KsTguiPalettes.Base);

    /// <summary>
    ///     <c>--tab-color</c>, <c>--color-text-translucent</c>.
    /// </summary>
    public static readonly Color TextColor = KsTguiPalettes.Translucent(Color.White, 0.5f, KsTguiPalettes.Base);

    /// <summary>
    ///     <c>--tab-color-selected</c>: the primary colour at lightness 90.
    /// </summary>
    public static readonly Color SelectedTextColor = Color.FromHex("#DCE5EF");

    private readonly KsTguiStyleBox _box = new() { RadiusTopLeft = 2f, RadiusBottomLeft = 2f };
    // Made with the fields rather than in the constructor: the base constructor already calls DrawModeChanged.
    private readonly Label _nameLabel = new() { HorizontalExpand = true, ClipText = true };
    private readonly Label _countLabel = new();

    private bool _selected;
    private float _wash;
    private float _bar;

    public KsTguiFabricatorTab()
    {
        StyleBoxOverride = _box;
        ModulateSelfOverride = Color.White; // the box carries its own colours, so the sheet's button tints must not reach it
        _box.SetContentMarginOverride(StyleBox.Margin.All, 6f);
        _box.BorderThickness = new Thickness(0f, 0f, IndicatorWidth, 0f);

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };
        row.AddChild(_nameLabel);
        row.AddChild(_countLabel);
        AddChild(row);

        Refresh(instant: true);
    }

    public string? Text
    {
        get => _nameLabel.Text;
        set => _nameLabel.Text = value;
    }

    /// <summary>
    ///     The number of designs; shown in brackets, as tg's.
    /// </summary>
    public int Count
    {
        set => _countLabel.Text = $"({value})";
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;

            _selected = value;
            Refresh(instant: false);
        }
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        Refresh(instant: false);
    }

    private float WashTarget => _selected || DrawMode == DrawModeEnum.Hover ? 1f : 0f;

    private float BarTarget => _selected ? 1f : 0f;

    private void Refresh(bool instant)
    {
        var textColor = _selected ? SelectedTextColor : TextColor;
        _nameLabel.FontColorOverride = textColor;
        _countLabel.FontColorOverride = textColor;

        if (instant)
        {
            _wash = WashTarget;
            _bar = BarTarget;
        }

        ApplyColors();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_wash == WashTarget && _bar == BarTarget)
            return;

        var step = args.DeltaSeconds / TransitionSeconds;
        _wash = MoveTowards(_wash, WashTarget, step);
        _bar = MoveTowards(_bar, BarTarget, step);
        ApplyColors();
    }

    private void ApplyColors()
    {
        // ease-out, on the way in and out alike.
        _box.BackgroundColor = Wash.WithAlpha(Wash.A * EaseOut(_wash));
        _box.BorderColor = KsTguiPalettes.PrimaryBase.WithAlpha(EaseOut(_bar));
    }

    private static float MoveTowards(float current, float target, float step)
    {
        return current < target ? MathF.Min(current + step, target) : MathF.Max(current - step, target);
    }

    /// <summary>
    ///     CSS <c>ease-out</c>, <c>cubic-bezier(0, 0, 0.58, 1)</c>, closely enough for a tenth of a second.
    /// </summary>
    private static float EaseOut(float progress)
    {
        var remaining = 1f - progress;
        return 1f - remaining * remaining;
    }
}
