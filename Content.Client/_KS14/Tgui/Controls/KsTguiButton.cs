using System.Numerics;
using System.Text;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     Which side of the text the icon of a <see cref="KsTguiButton"/> is on (tgui's <c>iconPosition</c>).
/// </summary>
public enum KsTguiIconPosition : byte
{
    Left,
    Right,
}

/// <summary>
///     tgui's <c>verticalAlignContent</c>.
/// </summary>
public enum KsTguiButtonContentAlign : byte
{
    Middle,
    Top,
    Bottom,
}

/// <summary>
///     tgui's Button: optional icon beside the text, a <see cref="KsTguiColor"/>, compact, fluid, circular and ellipsis forms,
///         and a selected state. Colours come from the sheetlet's per-colour classes, which also give the hover fade its
///         start colour (<see cref="KsTguiSheetlet.StylePropertyNormalColor"/>), so the button fades like any other.
/// </summary>
/// <remarks>
///     Text is at the left, as tgui's: its Button sets no <c>text-align</c> and so inherits <c>start</c>. Set
///         <see cref="LeftAligned"/> to false for centred text, which tgui windows ask for with <c>textAlign="center"</c>.
/// </remarks>
[Virtual]
public class KsTguiButton : Button
{
    /// <summary>
    ///     tgui's <c>.Button--icon</c> min-width, 1.333em.
    /// </summary>
    private const float IconMinWidth = 16f;

    /// <summary>
    ///     tgui's <c>--button-height</c>, which is also the line the text sits in.
    /// </summary>
    private const float LineHeight = 20f;

    private const float CompactLineHeight = 16f;

    /// <summary>
    ///     On a compact button, so the sheet can undo the 8pt label the engine's small button brings: tgui's compact keeps the
    ///         font size and only trims the padding.
    /// </summary>
    public const string StyleClassCompact = "KsTguiButtonCompact";

    /// <summary>
    ///     tgui's <c>--button-transition</c>.
    /// </summary>
    private const float TextFadeSeconds = 0.2f;

    /// <summary>
    ///     Where a strikethrough sits: Verdana's strikeout position is about 0.26em above the baseline.
    /// </summary>
    private const float StrikeEm = 0.26f;

    private const string Ellipsis = "…";

    private readonly KsTguiIcon _icon;
    private readonly StrikeLine _strike;

    private KsTguiColor _color;
    private bool _selected;
    private bool _compact;
    private bool _circular;
    private bool _ellipsis;
    private bool _strikethrough;
    private bool _leftAligned = true;
    private bool _inlineIcon;
    private string? _iconGlyph;
    private Color? _iconColor;
    private KsTguiIconPosition _iconPosition;
    private KsTguiButtonContentAlign _verticalAlign;
    private float _boxPadding = 6f;
    private bool _marginApplied;
    private Control? _watchedParent;

    // What the label says in full, while an ellipsis has cut it down. See ApplyEllipsis.
    private string? _fullText;
    private string? _cutText;

    // The text colour mid-fade on a transparent button.
    private Color? _textShown;
    private Color _textFrom;
    private Color _textTo;
    private float _textElapsed;

    public KsTguiButton()
    {
        _icon = new KsTguiIcon { Visible = false, IconColor = null, Align = Label.AlignMode.Center /* a narrow glyph, such as the X, sits in the middle of the icon's box */ };
        AddChild(_icon);

        _strike = new StrikeLine { Visible = false };
        AddChild(_strike);

        AddStyleClass(KsTguiColors.ButtonClass(_color));
        Label.AddStyleClass(KsTguiSheetlet.StyleClassButtonLabelLeft);

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

    /// <summary>
    ///     Shows <see cref="Icon"/> in Font Awesome's Regular face, as tgui's <c>-o</c> names (<c>square-o</c>).
    /// </summary>
    public bool IconRegular
    {
        get => _icon.Regular;
        set
        {
            _icon.Regular = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     For an icon that is a child of the button rather than its <c>icon</c> prop, as the chevron of a Collapsible: no
    ///         margin round it, so it sits at the button's padding and the text follows its 16px box directly.
    /// </summary>
    public bool InlineIcon
    {
        get => _inlineIcon;
        set
        {
            _inlineIcon = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>iconPosition</c>.
    /// </summary>
    public KsTguiIconPosition IconPosition
    {
        get => _iconPosition;
        set
        {
            _iconPosition = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>iconRotation</c>, in degrees.
    /// </summary>
    public float IconRotation
    {
        get => _icon.Rotation;
        set => _icon.Rotation = value;
    }

    /// <summary>
    ///     tgui's <c>iconSpin</c>.
    /// </summary>
    public bool IconSpin
    {
        get => _icon.Spin;
        set => _icon.Spin = value;
    }

    /// <summary>
    ///     tgui's <c>iconSize</c>, in ems.
    /// </summary>
    public float IconSize
    {
        get => _icon.IconSize;
        set
        {
            _icon.IconSize = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>iconColor</c>; null follows the text.
    /// </summary>
    public Color? IconColor
    {
        get => _iconColor;
        set
        {
            _iconColor = value;
            _icon.IconColor = value;
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
    ///     tgui's selected button: green, whatever its colour. A transparent one stays clear and only its text turns green,
    ///         as tgui's <c>Button.Checkbox</c> looks.
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
    ///     tgui's <c>transition: none</c> (SpawnSearch's list rows set it): the button changes colour at once, hover included.
    /// </summary>
    public bool Instant { get; set; }

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
            {
                AddStyleClass(StyleClass.ButtonSmall);
                AddStyleClass(StyleClassCompact);
            }
            else
            {
                RemoveStyleClass(StyleClass.ButtonSmall);
                RemoveStyleClass(StyleClassCompact);
            }

            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>circular</c>: a pill.
    /// </summary>
    public bool Circular
    {
        get => _circular;
        set
        {
            if (_circular == value)
                return;

            _circular = value;
            if (value)
                AddStyleClass(KsTguiSheetlet.StyleClassButtonCircular);
            else
                RemoveStyleClass(KsTguiSheetlet.StyleClassButtonCircular);
        }
    }

    /// <summary>
    ///     tgui's <c>ellipsis</c>: text that does not fit is cut short with "…". The button then reports the cut text from
    ///         <see cref="Button.Text"/> while it is cut; <see cref="FullText"/> is always what was set.
    /// </summary>
    public bool EllipsisText
    {
        get => _ellipsis;
        set
        {
            if (_ellipsis == value)
                return;

            _ellipsis = value;
            if (!value)
                RestoreFullText();

            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     The text as it was set, whether or not an ellipsis is cutting it.
    /// </summary>
    public string? FullText => _cutText != null && Label.Text == _cutText ? _fullText : Label.Text;

    /// <summary>
    ///     tgui's <c>verticalAlignContent</c>, for a button taller than its text.
    /// </summary>
    public KsTguiButtonContentAlign VerticalAlignContent
    {
        get => _verticalAlign;
        set
        {
            _verticalAlign = value;
            InvalidateArrange();
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
            RefreshMargin();
        }
    }

    /// <summary>
    ///     The text sits at the left (the default, as tgui's), with a clean cut at the end of a long one, instead of centred. A
    ///         style rule does this, because the button style's own alignment beats <see cref="Button.TextAlign"/>.
    /// </summary>
    public bool LeftAligned
    {
        get => _leftAligned;
        set
        {
            _leftAligned = value;
            if (value)
                Label.AddStyleClass(KsTguiSheetlet.StyleClassButtonLabelLeft);
            else
                Label.RemoveStyleClass(KsTguiSheetlet.StyleClassButtonLabelLeft);
        }
    }

    /// <summary>
    ///     tgui's <c>text-decoration: line-through</c> on the label: a 1px line of the label's colour, 0.26em above the
    ///         baseline.
    /// </summary>
    public bool Strikethrough
    {
        get => _strikethrough;
        set
        {
            _strikethrough = value;
            _strike.Visible = value;
        }
    }

    /// <summary>
    ///     Where the strikethrough's line is drawn, in the button's virtual pixels, or null when it is off or nothing is
    ///         laid out. For tests.
    /// </summary>
    public (float Left, float Right, float Y)? StrikeLineBounds => _strikethrough ? _strike.Line : null;

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        // Button restyles only its own label when its pseudo-class changes, so the icon would keep its enabled colour.
        _icon.Restyle();
        RefreshMargin();
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        _watchedParent = Parent;
        if (_watchedParent != null)
        {
            _watchedParent.OnChildAdded += OnSiblingsChanged;
            _watchedParent.OnChildRemoved += OnSiblingsChanged;
        }

        RefreshMargin();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        if (_watchedParent != null)
        {
            _watchedParent.OnChildAdded -= OnSiblingsChanged;
            _watchedParent.OnChildRemoved -= OnSiblingsChanged;
            _watchedParent = null;
        }
    }

    private void OnSiblingsChanged(Control _)
    {
        RefreshMargin();
    }

    /// <summary>
    ///     tgui's margins: 2px right and bottom, none on the right of a fluid button (its margin-left and -right are 0) and
    ///         none at all on the last child. In a horizontal box there is no bottom margin: tg's buttons in a row are inline blocks on a
    ///         shared baseline, so margin-bottom only adds room under the line and never shortens a button, while a box would stretch
    ///         each child into its slot minus the margin and leave every button but the last 2px short.
    /// </summary>
    private void RefreshMargin()
    {
        if (!TryGetStyleProperty<bool>(KsTguiSheetlet.StylePropertyRestyleChildren, out var tgui) || !tgui)
        {
            if (_marginApplied)
            {
                _marginApplied = false;
                Margin = default;
            }

            return;
        }

        var isLast = Parent is { ChildCount: > 0 } parent && parent.GetChild(parent.ChildCount - 1) == this;
        var inRow = Parent is BoxContainer { Orientation: BoxContainer.LayoutOrientation.Horizontal };
        var margin = isLast ? default : new Thickness(0f, 0f, Fluid ? 0f : 2f, inRow ? 0f : 2f);
        if (_marginApplied && Margin == margin)
            return;

        _marginApplied = true;
        Margin = margin;
    }

    private bool HasText => !string.IsNullOrEmpty(Label.Text);

    private bool IsTransparent => _color == KsTguiColor.Transparent;

    private void RefreshClasses()
    {
        for (var i = 0; i < KsTguiColors.Count; i++)
            RemoveStyleClass(KsTguiColors.ButtonClass((KsTguiColor)i));

        RemoveStyleClass(KsTguiSheetlet.StyleClassButtonSelected);
        RemoveStyleClass(KsTguiSheetlet.StyleClassButtonSelectedTransparent);
        if (_selected && IsTransparent)
            AddStyleClass(KsTguiSheetlet.StyleClassButtonSelectedTransparent);
        else if (_selected)
            AddStyleClass(KsTguiSheetlet.StyleClassButtonSelected);
        else
            AddStyleClass(KsTguiColors.ButtonClass(_color));
    }

    private float EffectiveLineHeight => _compact ? CompactLineHeight : LineHeight;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        UpdateTextFade(args.DeltaSeconds);
    }

    /// <summary>
    ///     A transparent button's text is its colour, and tgui fades it with the background (<c>transition-property: ...
    ///         color</c>). A rule can only switch it, so this eases the label towards what the rules say. Pressing is
    ///         instant, as tgui's <c>:active { transition: none }</c>.
    /// </summary>
    private void UpdateTextFade(float deltaSeconds)
    {
        if (Instant
            || !IsTransparent
            || !Label.TryGetStyleProperty<Color>(Label.StylePropertyFontColor, out var target))
        {
            if (_textShown != null)
            {
                _textShown = null;
                Label.FontColorOverride = null;
                _icon.IconColor = _iconColor;
            }

            return;
        }

        if (_textShown == null)
        {
            _textShown = target;
            _textFrom = target;
            _textTo = target;
            _textElapsed = TextFadeSeconds;
            return;
        }

        if (target != _textTo)
        {
            _textFrom = DrawMode == DrawModeEnum.Pressed ? target : _textShown.Value;
            _textTo = target;
            _textElapsed = DrawMode == DrawModeEnum.Pressed ? TextFadeSeconds : 0f;
        }

        _textElapsed += deltaSeconds;
        var progress = MathF.Min(_textElapsed / TextFadeSeconds, 1f);
        var remaining = 1f - progress;
        var shown = Robust.Shared.Maths.Color.InterpolateBetween(_textFrom, _textTo, 1f - remaining * remaining * remaining);
        _textShown = shown;

        Label.FontColorOverride = progress >= 1f ? null : shown;
        _icon.IconColor = _iconColor ?? (progress >= 1f ? null : shown);
    }

    private float IconMargin => _inlineIcon ? 0f : _compact ? 1f : 3f;

    private float IconWidth => MathF.Max(IconMinWidth, _icon.DesiredSize.X);

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        // First, so the label measures the text it will show.
        if (_ellipsis && !float.IsInfinity(availableSize.X))
            ApplyEllipsis(TextRoom(availableSize.X));

        var measured = base.MeasureOverride(availableSize);

        // base sized the box around the largest child; what is left over is the style box's own horizontal margins.
        var largest = MathF.Max(_icon.Visible ? _icon.DesiredSize.X : 0f, Label.DesiredSize.X);
        _boxPadding = MathF.Max(0f, (measured.X - largest) / 2f);

        if (!_icon.Visible)
            return measured;

        var textWidth = HasText ? Label.DesiredSize.X : 0f;

        // tgui drops the padding on the icon's side, and both sides when there is no text.
        float width;
        if (_iconPosition == KsTguiIconPosition.Right && HasText)
            width = _boxPadding + textWidth + 3f + IconWidth + 3f;
        else if (_inlineIcon)
            width = 2f * _boxPadding + IconWidth + textWidth;
        else
            width = 2f * IconMargin + IconWidth + textWidth + (HasText ? _boxPadding : 0f);

        return new Vector2(width, measured.Y);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var iconLeft = 0f;
        float labelLeft;
        float labelRight;
        if (!_icon.Visible)
        {
            labelLeft = _boxPadding;
            labelRight = finalSize.X - _boxPadding;
        }
        else if (_iconPosition == KsTguiIconPosition.Right && HasText)
        {
            iconLeft = finalSize.X - 3f - IconWidth;
            labelLeft = _boxPadding;
            labelRight = iconLeft - 3f;
        }
        else if (_inlineIcon)
        {
            iconLeft = _boxPadding;
            labelLeft = _boxPadding + IconWidth;
            labelRight = finalSize.X - _boxPadding;
        }
        else
        {
            iconLeft = IconMargin;
            labelLeft = 2f * IconMargin + IconWidth;
            labelRight = finalSize.X - (HasText ? _boxPadding : 0f);
        }

        labelRight = MathF.Max(labelLeft, labelRight);

        if (_ellipsis)
            ApplyEllipsis(labelRight - labelLeft);

        var lineTop = _verticalAlign switch
        {
            KsTguiButtonContentAlign.Top => 0f,
            KsTguiButtonContentAlign.Bottom => finalSize.Y - EffectiveLineHeight,
            _ => (finalSize.Y - EffectiveLineHeight) / 2f,
        };

        float Centered(float height)
        {
            return _verticalAlign == KsTguiButtonContentAlign.Middle
                ? (finalSize.Y - height) / 2f
                : lineTop + (EffectiveLineHeight - height) / 2f;
        }

        if (_icon.Visible)
            _icon.Arrange(UIBox2.FromDimensions(iconLeft, Centered(_icon.DesiredSize.Y), IconWidth, _icon.DesiredSize.Y));
        else
            _icon.Arrange(UIBox2.FromDimensions(0f, 0f, 0f, 0f));

        var labelTop = Centered(Label.DesiredSize.Y);
        Label.Arrange(new UIBox2(labelLeft, labelTop, labelRight, labelTop + Label.DesiredSize.Y));

        _strike.Arrange(new UIBox2(labelLeft, labelTop, labelRight, labelTop + Label.DesiredSize.Y));
        _strike.Place(this, labelLeft, labelRight, labelTop);

        ArrangeContent(new UIBox2(labelLeft, 0f, labelRight, finalSize.Y));

        return finalSize;
    }

    /// <summary>
    ///     For a subclass that adds a child of its own: place it in the space the text has.
    /// </summary>
    protected virtual void ArrangeContent(UIBox2 textBox)
    {
    }

    /// <summary>
    ///     The room the text has when the button is this wide.
    /// </summary>
    private float TextRoom(float buttonWidth)
    {
        var room = buttonWidth - _boxPadding * 2f;
        if (_icon.Visible)
            room -= HasText && _iconPosition == KsTguiIconPosition.Right ? 6f + IconWidth : 2f * IconMargin + IconWidth - _boxPadding;

        return room;
    }

    private void RestoreFullText()
    {
        if (_cutText != null && Label.Text == _cutText)
            Label.Text = _fullText;

        _cutText = null;
        _fullText = null;
    }

    /// <summary>
    ///     Cuts the label to this width with a trailing "…" if its full text is wider, and gives back the full text when it
    ///         fits again. The full text is remembered across edits: any text the label holds that is not our cut is the
    ///         new full text.
    /// </summary>
    private void ApplyEllipsis(float room)
    {
        var text = Label.Text ?? string.Empty;
        if (_cutText == null || text != _cutText)
            _fullText = text;

        var full = _fullText ?? string.Empty;
        var shown = full;
        if (TextWidth(full) > room)
        {
            var ellipsisWidth = TextWidth(Ellipsis);
            var low = 0;
            var high = full.Length;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (TextWidth(full[..middle]) + ellipsisWidth <= room)
                    low = middle;
                else
                    high = middle - 1;
            }

            shown = full[..low].TrimEnd() + Ellipsis;
        }

        _cutText = shown == full ? null : shown;
        if (Label.Text != shown)
            Label.Text = shown;
    }

    /// <summary>
    ///     The width of a string in the label's font, in virtual pixels.
    /// </summary>
    public float TextWidth(string text)
    {
        var font = LabelFont();
        var scale = UIScale;
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            if (font.TryGetCharMetrics(rune, scale, out var metrics))
                width += metrics.Advance;
        }

        return width / scale;
    }

    public Font LabelFont()
    {
        if (Label.FontOverride is { } font)
            return font;

        return Label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
            ? styled
            : UserInterfaceManager.ThemeDefaults.LabelFont;
    }

    public Color LabelColor()
    {
        if (Label.FontColorOverride is { } color)
            return color;

        return Label.TryGetStyleProperty<Color>(Label.StylePropertyFontColor, out var styled) ? styled : Robust.Shared.Maths.Color.White;
    }

    /// <summary>
    ///     The line of a <see cref="Strikethrough"/>.
    /// </summary>
    private sealed class StrikeLine : Control
    {
        private KsTguiButton? _owner;
        private float _left;
        private float _right;
        private float _top;

        public StrikeLine()
        {
            MouseFilter = MouseFilterMode.Ignore;
        }

        public (float Left, float Right, float Y)? Line => _owner == null ? null : (_left, _right, _top);

        public void Place(KsTguiButton owner, float labelLeft, float labelRight, float labelTop)
        {
            _owner = owner;
            var textWidth = MathF.Min(owner.TextWidth(owner.Label.Text ?? string.Empty), labelRight - labelLeft);
            var start = owner.Label.Align switch
            {
                Label.AlignMode.Left => labelLeft,
                Label.AlignMode.Right => labelRight - textWidth,
                _ => labelLeft + (labelRight - labelLeft - textWidth) / 2f,
            };

            var font = owner.LabelFont();
            var baseline = labelTop + font.GetAscent(owner.UIScale) / owner.UIScale;
            _left = start;
            _right = start + textWidth;
            _top = baseline - StrikeEm * 12f;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            if (_owner == null || _right <= _left)
                return;

            // Relative to the box the line was arranged in, which starts at the label's left edge and top.
            var scale = UIScale;
            var originX = PixelPosition.X;
            var originY = PixelPosition.Y;
            var x0 = _left * scale - originX;
            var x1 = _right * scale - originX;
            var y = MathF.Round(_top * scale) - originY;
            handle.DrawRect(new UIBox2(x0, y, x1, y + MathF.Max(1f, MathF.Round(scale))), _owner.LabelColor());
        }
    }
}
