using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's ImageButton: a picture (a prototype's sprite or a texture) on a card, with its text in a bar along the
///         bottom, or, <see cref="Fluid"/>, as a row with the picture at the left. Drawn by the control from its own state,
///         as tgui derives every shade from one colour (<c>ImageButton.scss</c>). Not ported: the glow while pressed, the
///         text outline on the corner labels, the divider lines between docked buttons, <c>contrast()</c> on a disabled
///         row, and the <c>title</c> prop (nothing in the ported windows uses it).
/// </summary>
/// <remarks>
///     The card is <see cref="ImageSize"/> + 8 wide at least, but takes a wider slot (the picture stays centred), so a
///         <see cref="KsTguiCardGrid"/> can share the width out. <see cref="Color"/> follows tgui's names, so
///         <see cref="KsTguiColor.Default"/> is its grey, not the Button's blue; that one is <see cref="KsTguiColor.Primary"/>.
/// </remarks>
[Virtual]
public class KsTguiImageButton : ContainerButton
{
    /// <summary>
    ///     tgui's default <c>imageSize</c>.
    /// </summary>
    public const float DefaultImageSize = 64f;

    /// <summary>
    ///     <c>--border-thickness-tiny</c>.
    /// </summary>
    private const float Border = 1f;

    /// <summary>
    ///     <c>--space-s</c>: the padding around the picture on a card.
    /// </summary>
    private const float ImagePadding = 3f;

    /// <summary>
    ///     <c>--border-radius-medium</c>.
    /// </summary>
    private const float Radius = 4f;

    /// <summary>
    ///     The title bar's padding, <c>0.25em 0.33em</c>.
    /// </summary>
    private const float BarPaddingX = 4f;

    private const float BarPaddingY = 3f;

    /// <summary>
    ///     The docked buttons of a card sit <c>1.8em</c> above its bottom.
    /// </summary>
    private const float ButtonsBottom = 21.6f;

    /// <summary>
    ///     A fluid row's content: <c>margin: 0 0.5em</c>, and <c>padding: 0.5em</c> on the text.
    /// </summary>
    private const float FluidMargin = 6f;

    private const float FluidPadding = 6f;

    /// <summary>
    ///     <c>--imagebutton-transparecy</c>.
    /// </summary>
    private const float BackgroundOpacity = 0.25f;

    private static readonly XColor Clear = XColor.Black.WithAlpha(0f);
    private static readonly XColor TransparentText = KsTguiPalettes.Translucent(XColor.White, 0.75f, KsTguiPalettes.Section);
    private static readonly XColor Dim = XColor.FromHex("#B4B4B4");
    private static readonly XColor Faded = new(1f, 1f, 1f, 0.5f);

    private readonly EntityPrototypeView _view;
    private readonly TextureRect _texture;
    private readonly KsTguiIcon _fallback;
    private readonly Label _label;
    private readonly BoxContainer _buttonsHost;
    private readonly BoxContainer _altHost;
    private readonly BoxContainer _cornerRow;
    private readonly Label _topLeft;
    private readonly Label _topRight;

    private readonly KsTguiStyleBox _outer = new(Clear, Radius);
    private readonly KsTguiStyleBox _bar = new(Clear) { BorderThickness = new Thickness(0f, Border, 0f, 0f) };

    private float _imageSize = DefaultImageSize;
    private bool _fluid;
    private bool _selected;
    private KsTguiColor _color;
    private Control? _content;
    private Control? _altButtons;
    private Control? _buttons;
    private bool _hasImage;
    private EntProtoId? _imagePrototype;

    // Where the last arrange put the title bar, for Draw.
    private float _barTop;
    private bool _built;
    private bool _labelOverflows;

    /// <summary>
    ///     A region of a texture. A sprite's frame is already an <see cref="AtlasTexture"/>, and the engine can't draw an atlas
    ///         of an atlas: it throws in the draw, every frame, which takes the whole UI down with it. So the region is
    ///         worked out against the frame's own source.
    /// </summary>
    public static Texture CropTexture(Texture texture, UIBox2 region)
    {
        return texture is AtlasTexture atlas
            ? new AtlasTexture(atlas.SourceTexture, region.Translated(atlas.SubRegion.TopLeft))
            : new AtlasTexture(texture, region);
    }

    public KsTguiImageButton()
    {
        var fonts = IoCManager.Resolve<KsTguiFonts>();
        StyleBoxOverride = new StyleBoxEmpty(); // drawn by Draw, which no style rule can change
        _bar.RadiusBottomLeft = Radius;
        _bar.RadiusBottomRight = Radius;

        _view = new EntityPrototypeView
        {
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
            Stretch = SpriteView.StretchMode.Fill, // fills the box whatever size the sprite is
            WorldRotation = Angle.Zero, // else the view sizes the sprite by its rotated diagonal and fills only 70%
        };
        _texture = new TextureRect
        {
            Visible = false,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            MouseFilter = MouseFilterMode.Ignore,
        };
        _fallback = new KsTguiIcon
        {
            Glyph = KsTguiIcons.Question,
            IconColor = KsTguiPalettes.TextDim,
            Align = Label.AlignMode.Center,
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
        };
        _label = new Label { ClipText = true, MouseFilter = MouseFilterMode.Ignore };

        _topLeft = new Label { FontOverride = fonts.GetFont(10), HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        _topRight = new Label { FontOverride = fonts.GetFont(10), FontColorOverride = Dim, MouseFilter = MouseFilterMode.Ignore };
        _cornerRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
            Margin = new Thickness(2f, 0f),
        };
        _cornerRow.AddChild(_topLeft);
        _cornerRow.AddChild(_topRight);

        _buttonsHost = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };
        _altHost = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };

        AddChild(_view);
        AddChild(_texture);
        AddChild(_fallback);
        AddChild(_label);
        AddChild(_altHost);
        AddChild(_buttonsHost);

        RefreshImage();
        RefreshAlt();
        _built = true;
        RefreshLook();
    }

    /// <summary>
    ///     A prototype's sprite as the picture (tgui's <c>dmIcon</c>).
    /// </summary>
    public EntProtoId? ImagePrototype
    {
        get => _imagePrototype;
        set
        {
            _imagePrototype = value;
            _view.SetPrototype(value);

            _view.Visible = value != null;
            RefreshImage();
        }
    }

    /// <summary>
    ///     A texture as the picture (tgui's <c>imageSrc</c>).
    /// </summary>
    public Texture? ImageTexture
    {
        get => _texture.Texture;
        set
        {
            _texture.Texture = value;
            _texture.Visible = value != null;
            RefreshImage();
        }
    }

    /// <summary>
    ///     The picture's side in pixels, 64 as tgui's default.
    /// </summary>
    public float ImageSize
    {
        get => _imageSize;
        set
        {
            _imageSize = value;
            _fallback.IconSize = value / 12f;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     The text in the bar along the bottom of a card, or beside the picture of a <see cref="Fluid"/> row.
    /// </summary>
    public string? Text
    {
        get => _label.Text;
        set
        {
            _label.Text = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     What a <see cref="Fluid"/> row shows in place of <see cref="Text"/>: tgui lets its children be anything. Mouse
    ///         filter of the control should be Ignore, or it will take the presses.
    /// </summary>
    public Control? Content
    {
        get => _content;
        set
        {
            _content?.Orphan();
            _content = value;
            if (value != null)
                AddChild(value);

            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     The text in the card's top left corner (the first half of tgui's <c>buttonsAlt</c> in the vending grid).
    /// </summary>
    public string? TopLeftText
    {
        get => _topLeft.Text;
        set
        {
            _topLeft.Text = value;
            RefreshAlt();
        }
    }

    /// <summary>
    ///     The dimmer text in the card's top right corner.
    /// </summary>
    public string? TopRightText
    {
        get => _topRight.Text;
        set
        {
            _topRight.Text = value;
            RefreshAlt();
        }
    }

    /// <summary>
    ///     tgui's <c>buttonsAlt</c>: overlaid along the top of a card, or docked at the right of a <see cref="Fluid"/>
    ///         row. Replaces the corner texts.
    /// </summary>
    public Control? AltButtons
    {
        get => _altButtons;
        set
        {
            _altButtons = value;
            RefreshAlt();
        }
    }

    /// <summary>
    ///     tgui's <c>buttons</c>: overlaid at the bottom left of a card above its bar, or docked at the right of a
    ///         <see cref="Fluid"/> row. Buttons in it take their own presses, and the card doesn't light up under them.
    /// </summary>
    public Control? Buttons
    {
        get => _buttons;
        set
        {
            _buttons?.Orphan();
            _buttons = value;
            if (value != null)
                _buttonsHost.AddChild(value);

            InvalidateMeasure();
        }
    }

    public KsTguiColor Color
    {
        get => _color;
        set
        {
            _color = value;
            RefreshLook();
        }
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            RefreshLook();
        }
    }

    /// <summary>
    ///     A row across the width, with the picture at the left, instead of a card.
    /// </summary>
    public bool Fluid
    {
        get => _fluid;
        set
        {
            _fluid = value;
            HorizontalExpand = value;
            RefreshAlt();
            RefreshLook();
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     The width a card needs at least.
    /// </summary>
    public float CardWidth => _imageSize + 2f * ImagePadding + 2f * Border;

    private bool HasBar => !_fluid && !string.IsNullOrEmpty(_label.Text);

    private void RefreshImage()
    {
        _hasImage = _view.Visible || _texture.Visible;
        _fallback.Visible = !_hasImage;
        InvalidateMeasure();
    }

    private void RefreshAlt()
    {
        if (!_built)
            return;

        _altHost.RemoveAllChildren();
        if (_altButtons != null)
        {
            _altButtons.Orphan();
            _altHost.AddChild(_altButtons);
        }
        else if (!_fluid && (!string.IsNullOrEmpty(_topLeft.Text) || !string.IsNullOrEmpty(_topRight.Text)))
        {
            _cornerRow.Orphan();
            _altHost.AddChild(_cornerRow);
        }

        InvalidateMeasure();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        RefreshLook();
    }

    /// <summary>
    ///     The text colour and fade, which are properties of the children; the box is drawn in <see cref="Draw"/>.
    /// </summary>
    private void RefreshLook()
    {
        if (!_built)
            return;

        var transparent = _color == KsTguiColor.Transparent && !_selected;
        if (_fluid)
            _label.FontColorOverride = transparent ? TransparentText : XColor.White;
        else if (_selected)
            _label.FontColorOverride = XColor.White;
        else
            _label.FontColorOverride = _color == KsTguiColor.Transparent ? TransparentText : KsTguiColors.Text(_color);

        var fade = Disabled ? Faded : XColor.White;
        _view.Modulate = fade;
        _texture.Modulate = fade;
        _fallback.Modulate = fade;
        _label.Modulate = fade;
        if (_content != null)
            _content.Modulate = fade;
    }

    /// <summary>
    ///     The shades <c>ImageButton.scss</c> derives from the colour: the background and border at CSS opacity .25, lightness
    ///         -10 and +5 (+5 and +5 hovered), and the title bar solid at -10 under a border of the colour itself.
    /// </summary>
    private (XColor Back, XColor Border, XColor Bar, XColor Token) ResolveShades()
    {
        var hovered = !Disabled && DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed;

        if (_color == KsTguiColor.Transparent && !_selected)
        {
            var back = Disabled
                ? KsTguiPalettes.Translucent(KsTguiColors.Shade(KsTguiColor.Red, 0f), 0.15f, KsTguiPalettes.Section)
                : hovered ? KsTguiPalettes.Translucent(XColor.White, 0.1f, KsTguiPalettes.Section) : Clear;
            return (back, Clear, Clear, Clear);
        }

        XColor Shifted(float shift)
        {
            if (_selected)
                return KsTguiColors.Shade(KsTguiColor.Green, 6f + shift /* the token at lightness x1.15 */);

            return _color == KsTguiColor.Default
                ? KsTguiColors.FromHsl(0f, 0f, 30f + shift)
                : KsTguiColors.Shade(_color, shift);
        }

        if (_selected && _color == KsTguiColor.Transparent)
        {
            var wash = KsTguiPalettes.Translucent(KsTguiPalettes.Good, 0.15f, KsTguiPalettes.Section);
            return (wash, Clear, Clear, Clear);
        }

        var background = KsTguiPalettes.Translucent(Shifted(hovered ? 5f : -10f), BackgroundOpacity, KsTguiPalettes.Section);
        var border = KsTguiPalettes.Translucent(Shifted(5f), BackgroundOpacity, KsTguiPalettes.Section);
        return (background, border, Shifted(-10f), Shifted(0f));
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _view.Measure(new Vector2(_imageSize, _imageSize));
        _texture.Measure(new Vector2(_imageSize, _imageSize));
        _fallback.Measure(new Vector2(_imageSize, _imageSize));

        if (_fluid)
            return MeasureFluid(availableSize);

        var width = CardWidth;
        _label.ClipText = false; // a clipping label reports no width of its own
        _label.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        _label.ClipText = true;
        _labelOverflows = _label.DesiredSize.X > width - 2f * BarPaddingX; // Measure cuts a desired size down to what it is given
        _label.Measure(new Vector2(MathF.Max(0f, width - 2f * BarPaddingX), float.PositiveInfinity));
        _altHost.Measure(new Vector2(width - 2f * Border, float.PositiveInfinity));
        _buttonsHost.Measure(new Vector2(width - 2f * Border, float.PositiveInfinity));
        _content?.Measure(Vector2.Zero);

        var imageHeight = _imageSize + 2f * ImagePadding + (HasBar ? Border : 2f * Border);
        var barHeight = HasBar ? Border + 2f * BarPaddingY + _label.DesiredSize.Y : 0f;
        return new Vector2(width, imageHeight + barHeight);
    }

    private Vector2 MeasureFluid(Vector2 availableSize)
    {
        _buttonsHost.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        _altHost.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        var dock = _buttonsHost.DesiredSize.X + _altHost.DesiredSize.X;
        var chrome = _imageSize + 2f * Border + 2f * FluidMargin + 2f * FluidPadding + dock;

        var content = _content ?? _label;
        content.Measure(new Vector2(MathF.Max(0f, availableSize.X - chrome), float.PositiveInfinity));
        if (_content != null)
            _label.Measure(Vector2.Zero);

        var height = MathF.Max(_imageSize, content.DesiredSize.Y + 2f * FluidPadding) + 2f * Border;
        return new Vector2(chrome + content.DesiredSize.X, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        return _fluid ? ArrangeFluid(finalSize) : ArrangeCard(finalSize);
    }

    private Vector2 ArrangeCard(Vector2 finalSize)
    {
        var hasBar = HasBar;
        var imageHeight = _imageSize + 2f * ImagePadding + (hasBar ? Border : 2f * Border);
        _barTop = hasBar ? imageHeight : finalSize.Y;

        var imageBox = UIBox2.FromDimensions(
            MathF.Floor((finalSize.X - _imageSize) / 2f), Border + ImagePadding, _imageSize, _imageSize);
        ArrangePicture(imageBox);

        _label.Visible = hasBar;
        if (hasBar)
        {
            var labelHeight = MathF.Min(_label.DesiredSize.Y, MathF.Max(0f, finalSize.Y - _barTop));
            // Centred, but a name too long for the bar starts at the left as tgui's ellipsis does, not cut on both sides.
            _label.Align = _labelOverflows ? Label.AlignMode.Left : Label.AlignMode.Center;
            _label.Arrange(UIBox2.FromDimensions(
                BarPaddingX, _barTop + Border + BarPaddingY, MathF.Max(0f, finalSize.X - 2f * BarPaddingX), labelHeight));
        }

        _altHost.Arrange(UIBox2.FromDimensions(Border, Border, MathF.Max(0f, finalSize.X - 2f * Border), _altHost.DesiredSize.Y));

        var buttonsBottom = hasBar ? finalSize.Y - ButtonsBottom : finalSize.Y;
        var buttonsLeft = hasBar ? Border : 0f;
        _buttonsHost.Arrange(UIBox2.FromDimensions(
            buttonsLeft, buttonsBottom - _buttonsHost.DesiredSize.Y, MathF.Min(_buttonsHost.DesiredSize.X, finalSize.X - buttonsLeft), _buttonsHost.DesiredSize.Y));

        if (_content != null)
            _content.Visible = false;

        return finalSize;
    }

    private Vector2 ArrangeFluid(Vector2 finalSize)
    {
        _barTop = finalSize.Y;
        var rowHeight = MathF.Max(0f, finalSize.Y - 2f * Border);

        ArrangePicture(UIBox2.FromDimensions(Border, Border + MathF.Floor((rowHeight - _imageSize) / 2f), _imageSize, _imageSize));

        var buttonsWidth = _buttonsHost.DesiredSize.X;
        var altWidth = _altHost.DesiredSize.X;
        _altHost.Arrange(UIBox2.FromDimensions(finalSize.X - Border - altWidth, Border, altWidth, rowHeight));
        _buttonsHost.Arrange(UIBox2.FromDimensions(finalSize.X - Border - altWidth - buttonsWidth, Border, buttonsWidth, rowHeight));

        var contentLeft = Border + _imageSize + FluidMargin + FluidPadding;
        var contentRight = finalSize.X - Border - altWidth - buttonsWidth - FluidMargin - FluidPadding;
        var content = _content ?? _label;
        var contentHeight = MathF.Min(content.DesiredSize.Y, rowHeight);
        content.Visible = true;
        _label.Visible = _content == null;
        _label.Align = Label.AlignMode.Left;
        content.Arrange(UIBox2.FromDimensions(
            contentLeft, Border + (rowHeight - contentHeight) / 2f, MathF.Max(0f, contentRight - contentLeft), contentHeight));
        return finalSize;
    }

    private void ArrangePicture(UIBox2 box)
    {
        _view.Arrange(box);
        _texture.Arrange(box);
        _fallback.Arrange(UIBox2.FromDimensions(box.Left, box.Top + (box.Height - _fallback.DesiredSize.Y) / 2f, box.Width, _fallback.DesiredSize.Y));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var scale = UIScale;
        var box = PixelSizeBox;
        var (back, border, barBack, token) = ResolveShades();

        _outer.BackgroundColor = back;
        _outer.BorderColor = border;
        _outer.BorderThickness = _fluid || !HasBar ? new Thickness(Border) : new Thickness(0f);
        _outer.Draw(handle, box, scale);

        if (_fluid || border.A <= 0f && barBack.A <= 0f)
            return;

        var fade = Disabled ? Faded : XColor.White;
        handle.Modulate = fade;

        if (HasBar)
        {
            // The picture's own border runs down both sides and across the top, stopping short of the rounded corners.
            var inset = 2f * scale;
            var line = MathF.Max(1f, MathF.Round(scale * Border));
            var imageBottom = _barTop * scale;

            // Drawn once before it is laid out, at no size: these boxes would invert.
            if (box.Width < 2f * inset || imageBottom < inset || box.Height < imageBottom)
            {
                handle.Modulate = XColor.White;
                return;
            }

            handle.DrawRect(new UIBox2(inset, 0f, box.Width - inset, line), border);
            handle.DrawRect(new UIBox2(0f, inset, line, imageBottom), border);
            handle.DrawRect(new UIBox2(box.Width - line, inset, box.Width, imageBottom), border);

            _bar.BackgroundColor = barBack;
            _bar.BorderColor = token;
            _bar.Draw(handle, new UIBox2(0f, imageBottom, box.Width, box.Height), scale);
        }

        handle.Modulate = XColor.White;
    }
}
