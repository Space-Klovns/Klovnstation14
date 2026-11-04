using System.Numerics;
using Content.Client._KS14.UserInterface.Styles;
using Content.Client.Guidebook;
using Content.Client.Resources;
using Content.Shared.Guidebook;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     tgui's Window: a 32px grey title bar with the green interaction eye, the title and a close cross that turns red
///         on hover, over a dark vertical gradient with the Nanotrasen logo watermarked in the middle.
///         Xaml children go into the padded content column, which stacks them 6px apart like tgui's sections.
/// </summary>
[Virtual]
public partial class KsTguiWindow : BaseWindow
{
    private const float DragMarginSize = 7f;

    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private IEntitySystemManager _entitySystemManager = default!;

    private readonly Label _titleLabel;
    private readonly KsTguiButton _helpButton;
    private List<ProtoId<GuideEntryPrototype>>? _helpGuidebookIds;

    public KsTguiWindow()
    {
        IoCManager.InjectDependencies(this);

        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(200f, 100f);

        var background = new PanelContainer
        {
            PanelOverride = new KsStyleBoxGradient(KsTguiTheme.WindowTop, KsTguiTheme.WindowBottom, KsGradientDirection.TopToBottom),
            MouseFilter = MouseFilterMode.Ignore,
        };

        var statusIcon = new KsTguiIcon
        {
            Kind = KsTguiIconKind.Eye,
            Color = KsTguiTheme.Foreground(KsTguiTheme.Good),
            SetSize = new Vector2(20f, 20f),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };

        _titleLabel = new Label
        {
            HorizontalExpand = true,
            ClipText = true,
            VAlign = Label.VAlignMode.Center,
            FontOverride = KsTguiTheme.RegularFont(_resourceCache, 14),
            FontColorOverride = KsTguiTheme.TitleText,
        };

        CloseButton = new KsTguiCloseButton();
        CloseButton.OnPressed += _ => Close();

        _helpButton = new KsTguiButton
        {
            Text = "?",
            ButtonColor = KsTguiButtonColor.Transparent,
            VerticalAlignment = VAlignment.Center,
            Visible = false,
        };
        _helpButton.OnPressed += _ => Help();

        var titleBar = new PanelContainer
        {
            MinHeight = KsTguiTheme.TitleBarHeight,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = KsTguiTheme.TitleBar,
                BorderColor = Color.Black.WithAlpha(0.33f),
                BorderThickness = new Thickness(0f, 0f, 0f, 1f),
            },
            Children =
            {
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    Children =
                    {
                        new Control { MinWidth = 45f, Children = { statusIcon } },
                        _titleLabel,
                        _helpButton,
                        CloseButton,
                    },
                },
            },
        };

        Contents = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(6f),
            SeparationOverride = 6,
        };

        var body = new Control
        {
            VerticalExpand = true,
            RectClipContent = true,
            Children =
            {
                new KsTguiWatermark(),
                Contents,
            },
        };

        AddChild(background);
        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children = { titleBar, body },
        });

        XamlChildren = Contents.Children;
    }

    /// <summary>
    ///     The padded column the window's contents live in.
    /// </summary>
    public BoxContainer Contents { get; }

    public KsTguiCloseButton CloseButton { get; }

    public string? Title
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    /// <summary>
    ///     Guidebook entries the title bar's help button opens. Null hides the button. tgui has no such button; the
    ///         windows it replaces here had one.
    /// </summary>
    public List<ProtoId<GuideEntryPrototype>>? HelpGuidebookIds
    {
        get => _helpGuidebookIds;
        set
        {
            _helpGuidebookIds = value;
            _helpButton.Visible = value != null;
        }
    }

    private void Help()
    {
        if (_helpGuidebookIds != null)
            _entitySystemManager.GetEntitySystem<GuidebookSystem>().OpenHelp(_helpGuidebookIds);
    }

    protected override DragMode GetDragModeFor(Vector2 relativeMousePos)
    {
        var mode = DragMode.None;

        if (Resizable)
        {
            if (relativeMousePos.Y < DragMarginSize)
                mode = DragMode.Top;
            else if (relativeMousePos.Y > Size.Y - DragMarginSize)
                mode = DragMode.Bottom;

            if (relativeMousePos.X < DragMarginSize)
                mode |= DragMode.Left;
            else if (relativeMousePos.X > Size.X - DragMarginSize)
                mode |= DragMode.Right;
        }

        if (mode == DragMode.None && relativeMousePos.Y < KsTguiTheme.TitleBarHeight)
            mode = DragMode.Move;

        return mode;
    }
}

/// <summary>
///     The title bar's close button: a half-transparent cross that turns white on a red block when hovered.
/// </summary>
public sealed class KsTguiCloseButton : ContainerButton
{
    private static readonly StyleBoxFlat Idle = new() { BackgroundColor = Color.Black.WithAlpha(0f) };
    private static readonly StyleBoxFlat Hovered = new() { BackgroundColor = KsTguiTheme.Red };

    // Initialized inline: the ContainerButton constructor calls DrawModeChanged before ours runs.
    private readonly KsTguiIcon _cross = new()
    {
        Kind = KsTguiIconKind.Times,
        SetSize = new Vector2(14f, 14f),
        HorizontalAlignment = HAlignment.Center,
        VerticalAlignment = VAlignment.Center,
    };

    public KsTguiCloseButton()
    {
        MinWidth = 45f;
        ModulateSelfOverride = Color.White;
        AddChild(_cross);
        DrawModeChanged();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();

        var hovered = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed;
        StyleBoxOverride = hovered ? Hovered : Idle;
        _cross.Color = hovered ? Color.White : Color.White.WithAlpha(0.5f);
    }
}

/// <summary>
///     The Nanotrasen logo tgui draws behind window contents: black at a third opacity, fitted inside the middle 70%.
/// </summary>
public sealed partial class KsTguiWatermark : Control
{
    private const string TexturePath = "/Textures/_KS14/Interface/Tgui/nanotrasen.png";
    private const float Fill = 0.7f;

    [Dependency] private IResourceCache _resourceCache = default!;

    private readonly Texture _texture;

    public KsTguiWatermark()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Ignore;
        _texture = _resourceCache.GetTexture(TexturePath);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var available = (Vector2)PixelSize * Fill;
        var textureSize = (Vector2)_texture.Size;
        if (available.X <= 0f || available.Y <= 0f)
            return;

        var scale = MathF.Min(available.X / textureSize.X, available.Y / textureSize.Y);
        var size = textureSize * scale;
        var topLeft = ((Vector2)PixelSize - size) / 2f;
        handle.DrawTextureRect(_texture, UIBox2.FromDimensions(topLeft, size), KsTguiTheme.Watermark);
    }
}
