using System.Numerics;
using Content.Client.Stylesheets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's Button: optional icon in front of the text, a <see cref="KsTguiColor"/>, compact and fluid forms, and a
///         selected state. Colours come from the sheetlet's per-colour classes, which also give the hover fade its
///         start colour (<see cref="KsTguiSheetlet.StylePropertyNormalColor"/>), so the button fades like any other.
/// </summary>
public sealed class KsTguiButton : Button
{
    /// <summary>
    ///     tgui's <c>.Button--icon</c> min-width, 1.333em.
    /// </summary>
    private const float IconMinWidth = 16f;

    private readonly KsTguiIcon _icon;

    private KsTguiColor _color;
    private bool _selected;
    private bool _compact;
    private string? _iconGlyph;
    private float _boxPadding;

    public KsTguiButton()
    {
        _icon = new KsTguiIcon { Visible = false, IconColor = null, Align = Label.AlignMode.Center /* a narrow glyph, such as the X, sits in the middle of the icon's box */ };
        AddChild(_icon);

        AddStyleClass(KsTguiColors.ButtonClass(_color));

        // tgui's button is inline-block: it takes its content's width unless fluid.
        HorizontalAlignment = HAlignment.Left;
    }

    /// <summary>
    ///     The glyph in front of the text, normally a <see cref="KsTguiIcons"/> constant.
    /// </summary>
    public string? Icon
    {
        get => _iconGlyph;
        set
        {
            _iconGlyph = value;
            _icon.Glyph = value;
            _icon.Visible = !string.IsNullOrEmpty(value);
            InvalidateMeasure();
        }
    }

    public KsTguiColor Color
    {
        get => _color;
        set
        {
            if (_color == value)
                return;

            _color = value;
            RefreshClasses();
        }
    }

    /// <summary>
    ///     tgui's selected button: green, whatever its colour.
    /// </summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;

            _selected = value;
            RefreshClasses();
        }
    }

    /// <summary>
    ///     Less padding, as tgui's <c>compact</c>.
    /// </summary>
    public bool Compact
    {
        get => _compact;
        set
        {
            if (_compact == value)
                return;

            _compact = value;
            if (value)
                AddStyleClass(StyleClass.ButtonSmall);
            else
                RemoveStyleClass(StyleClass.ButtonSmall);

            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     As tgui's <c>fluid</c>: fills the width it is given.
    /// </summary>
    public bool Fluid
    {
        get => HorizontalExpand;
        set
        {
            HorizontalExpand = value;
            HorizontalAlignment = value ? HAlignment.Stretch : HAlignment.Left;
        }
    }

    /// <summary>
    ///     The text sits at the left, with a clean cut at the end of a long one, instead of centred. A style rule does this, because
    ///         the button style's own alignment beats <see cref="Button.TextAlign"/>.
    /// </summary>
    public bool LeftAligned
    {
        get => Label.HasStyleClass(KsTguiSheetlet.StyleClassButtonLabelLeft);
        set
        {
            if (value)
                Label.AddStyleClass(KsTguiSheetlet.StyleClassButtonLabelLeft);
            else
                Label.RemoveStyleClass(KsTguiSheetlet.StyleClassButtonLabelLeft);
        }
    }

    private bool HasText => !string.IsNullOrEmpty(Label.Text);

    private void RefreshClasses()
    {
        for (var i = 0; i < KsTguiColors.Count; i++)
            RemoveStyleClass(KsTguiColors.ButtonClass((KsTguiColor)i));

        RemoveStyleClass(KsTguiSheetlet.StyleClassButtonSelected);
        if (_selected)
            AddStyleClass(KsTguiSheetlet.StyleClassButtonSelected);
        else
            AddStyleClass(KsTguiColors.ButtonClass(_color));
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        if (!_icon.Visible)
            return measured;

        // base sized the box around the largest child; what is left over is the style box's own horizontal margins.
        var largest = MathF.Max(_icon.DesiredSize.X, Label.DesiredSize.X);
        _boxPadding = MathF.Max(0f, (measured.X - largest) / 2f);

        var iconMargin = Compact ? 1f : 3f;
        var iconWidth = MathF.Max(IconMinWidth, _icon.DesiredSize.X);
        var textWidth = HasText ? Label.DesiredSize.X : 0f;

        // tgui drops the padding on the icon's side, and both sides when there is no text.
        var width = 2f * iconMargin + iconWidth + textWidth + (HasText ? _boxPadding : 0f);
        return new Vector2(width, measured.Y);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (!_icon.Visible)
            return base.ArrangeOverride(finalSize);

        var iconMargin = Compact ? 1f : 3f;
        var iconWidth = MathF.Max(IconMinWidth, _icon.DesiredSize.X);
        var iconTop = (finalSize.Y - _icon.DesiredSize.Y) / 2f;
        _icon.Arrange(UIBox2.FromDimensions(iconMargin, iconTop, iconWidth, _icon.DesiredSize.Y));

        var labelLeft = 2f * iconMargin + iconWidth;
        var labelRight = finalSize.X - (HasText ? _boxPadding : 0f);
        var labelTop = (finalSize.Y - Label.DesiredSize.Y) / 2f;
        Label.Arrange(new UIBox2(labelLeft, labelTop, MathF.Max(labelLeft, labelRight), labelTop + Label.DesiredSize.Y));

        return finalSize;
    }
}
