using System.Numerics;
using Content.Client.Guidebook;
using Content.Client.Resources;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.CCVar;
using Content.Shared.Guidebook;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     Applies tgui's base window layout to every <see cref="DefaultWindow"/> and <see cref="FancyWindow"/> as it opens: the
///         title bar, close button and drag behaviour. No stylesheet can do this, since it changes the windows' controls,
///         and <see cref="DefaultWindow"/> is the engine's. Windows on any other base, like the Nuke's, are left alone.
/// </summary>
public sealed partial class KsTguiChromeUIController : UIController
{
    private const float TitleLeftMargin = 12f;

    /// <summary>
    ///     tgui's content padding. Only replaces the engine's default, since a window may have chosen its own.
    /// </summary>
    private const float ContentsMargin = 6f;

    private const float DefaultContentsMargin = 10f;

    /// <summary>
    ///     What was changed on a window, and so what to put back. Held on the window, which a dictionary here would keep
    ///         alive after closing. A value written from code beats the stylesheet for good, so everything is restored by hand.
    /// </summary>
    private sealed class Chrome
    {
        public required Control Header;
        public required Vector2 HeaderMinSize;
        public required Label Title;
        public required Thickness TitleMargin;
        public required TextureButton StockCloseButton;
        public required KsTguiTitleBarButton CloseButton;
        public KsTguiTitleBarButton? HelpButton;
        public Control? Contents;
        public Control? Handle;
    }

    private static readonly AttachedProperty<Chrome?> ChromeProperty =
        AttachedProperty<Chrome?>.CreateNull("KsTguiChrome", typeof(KsTguiChromeUIController));

    private static readonly AttachedProperty<List<ProtoId<GuideEntryPrototype>>?> GuidesProperty =
        AttachedProperty<List<ProtoId<GuideEntryPrototype>>?>.CreateNull("KsTguiGuides", typeof(KsTguiChromeUIController));

    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;

    private BaseWindow? _altDragWindow;
    private Vector2 _altDragOffset;

    private bool Enabled => _configurationManager.GetCVar(KsCCVars.TguiStyle);

    public override void Initialize()
    {
        base.Initialize();

        UIManager.WindowRoot.OnChildAdded += SyncWindow;
        UIManager.OnKeyBindDown += OnKeyBindDown;
    }

    /// <summary>
    ///     Brings every open window in line with the theme. A closed one catches up when it's opened again.
    /// </summary>
    public void SyncOpenWindows()
    {
        foreach (var child in UIManager.WindowRoot.Children)
            SyncWindow(child);
    }

    /// <summary>
    ///     Whether a window has the tgui chrome on it.
    /// </summary>
    public static bool HasChrome(BaseWindow window)
    {
        return window.GetValue(ChromeProperty) != null;
    }

    /// <summary>
    ///     Whether a <see cref="DefaultWindow"/> has a guidebook button in its title bar. A <see cref="FancyWindow"/>'s is
    ///         <see cref="FancyWindow.KsChromeHelpButton"/>.
    /// </summary>
    public static bool HasHelpButton(DefaultWindow window)
    {
        return window.GetValue(ChromeProperty)?.HelpButton != null;
    }

    /// <summary>
    ///     The guides a <see cref="DefaultWindow"/>'s title bar opens. <see cref="FancyWindow"/> has a button of its own, and the
    ///         engine's window has none, so this is a part of the chrome: it comes and goes with the theme.
    /// </summary>
    public void SetGuides(DefaultWindow window, List<ProtoId<GuideEntryPrototype>> guides)
    {
        window.SetValue(GuidesProperty, guides);

        if (window.GetValue(ChromeProperty) is { } chrome)
            AddHelpButton(chrome, () => OpenGuides(guides));
    }

    private void OpenGuides(List<ProtoId<GuideEntryPrototype>> guides)
    {
        EntityManager.System<GuidebookSystem>().OpenHelp(guides);
    }

    /// <summary>
    ///     The same control as the close button, beside it, so the two match and are spaced by their cells. The stock one on a
    ///         <see cref="FancyWindow"/> is a small texture that has neither.
    /// </summary>
    private void AddHelpButton(Chrome chrome, Action pressed)
    {
        if (chrome.HelpButton != null)
            return;

        chrome.HelpButton = new KsTguiTitleBarButton(
            _resourceCache.GetTexture(KsTguiSheetlet.TextureRoot / "question.png"),
            KsTguiTitleBarButton.NeutralHoverColor);
        chrome.HelpButton.OnPressed += _ => pressed();

        chrome.CloseButton.Parent!.AddChild(chrome.HelpButton);
        chrome.HelpButton.SetPositionInParent(chrome.CloseButton.GetPositionInParent());
    }

    private void SyncWindow(Control control)
    {
        // A window opened again may have come back after a rebuild, with the sheet of the old one.
        KsTguiWindowThemes.Reapply(control);

        if (control is not (DefaultWindow or FancyWindow))
            return;

        var window = (BaseWindow)control;
        var chrome = window.GetValue(ChromeProperty);

        if (Enabled && chrome == null)
            Attach(window);
        else if (!Enabled && chrome != null)
            Detach(window, chrome);
    }

    private void Attach(BaseWindow window)
    {
        Chrome? chrome = null;
        if (window is FancyWindow fancyWindow)
            chrome = MakeChrome(fancyWindow.KsHeader, fancyWindow.KsTitleLabel, fancyWindow.KsCloseButton);
        else if (window is DefaultWindow defaultWindow)
            chrome = AttachDefaultWindow(defaultWindow);

        if (chrome == null)
            return;

        chrome.Header.MinSize = new Vector2(0f, KsTguiGeometry.TitleBarHeight);
        chrome.Title.Margin = new Thickness(TitleLeftMargin, 0f, 0f, 0f);

        chrome.StockCloseButton.Visible = false;
        chrome.StockCloseButton.Parent?.AddChild(chrome.CloseButton);
        chrome.CloseButton.OnPressed += _ => window.Close();

        if (window is FancyWindow chromedFancyWindow)
            chromedFancyWindow.KsChromed = true;

        window.SetValue(ChromeProperty, chrome);

        if (window is FancyWindow helpFancyWindow)
        {
            AddHelpButton(chrome, helpFancyWindow.Help);
            helpFancyWindow.KsChromeHelpButton = chrome.HelpButton;
        }
        else if (window.GetValue(GuidesProperty) is { } guides)
        {
            AddHelpButton(chrome, () => OpenGuides(guides));
        }
    }

    private Chrome? AttachDefaultWindow(DefaultWindow window)
    {
        var header = FindByClass<PanelContainer>(window, window.Contents, DefaultWindow.StyleClassWindowHeader);
        var title = FindByClass<Label>(window, window.Contents, DefaultWindow.StyleClassWindowTitle);
        var closeButton = FindByClass<TextureButton>(window, window.Contents, DefaultWindow.StyleClassWindowCloseButton);

        if (header == null || title == null || closeButton == null)
            return null;

        var chrome = MakeChrome(header, title, closeButton);

        if (window.Contents.Margin == new Thickness(DefaultContentsMargin))
        {
            window.Contents.Margin = new Thickness(ContentsMargin);
            chrome.Contents = window.Contents;
        }

        chrome.Handle = new KsTguiWindowHandle(window);
        window.AddChild(chrome.Handle);
        chrome.Handle.SetPositionInParent(1);

        return chrome;
    }

    private Chrome MakeChrome(Control header, Label title, TextureButton closeButton)
    {
        var icon = _resourceCache.GetTexture(KsTguiSheetlet.TextureRoot / "cross.png");

        return new Chrome
        {
            Header = header,
            HeaderMinSize = header.MinSize,
            Title = title,
            TitleMargin = title.Margin,
            StockCloseButton = closeButton,
            CloseButton = new KsTguiTitleBarButton(icon, KsTguiTitleBarButton.CloseHoverColor),
        };
    }

    private static void Detach(BaseWindow window, Chrome chrome)
    {
        chrome.Header.MinSize = chrome.HeaderMinSize;
        chrome.Title.Margin = chrome.TitleMargin;
        chrome.StockCloseButton.Visible = true;
        chrome.CloseButton.Orphan();
        chrome.HelpButton?.Orphan();
        chrome.Handle?.Orphan();

        if (chrome.Contents is { } contents)
            contents.Margin = new Thickness(DefaultContentsMargin);

        if (window is FancyWindow fancyWindow)
        {
            fancyWindow.KsChromed = false;
            fancyWindow.KsChromeHelpButton = null;
        }

        window.SetValue(ChromeProperty, null);
    }

    /// <summary>
    ///     Finds a part of a window's frame. By style class and not <see cref="Control.FindControl{T}"/>, whose names are
    ///         shared with the window's own XAML, so a subclass with a control of the same name would answer instead.
    /// </summary>
    private static T? FindByClass<T>(Control root, Control contents, string styleClass) where T : Control
    {
        foreach (var child in root.Children)
        {
            if (child == contents)
                continue;

            if (child is T match && child.HasStyleClass(styleClass))
                return match;

            if (FindByClass<T>(child, contents, styleClass) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>
    ///     tgui's Alt-drag, from anywhere in a window. In game, Alt with the left mouse button isn't a UI click at all, since
    ///         a longer binding uses it, so this asks about the keys themselves. This event also fires for every other key
    ///         held over a window, which that weeds out.
    /// </summary>
    private void OnKeyBindDown(Control control)
    {
        if (_altDragWindow != null || !Enabled)
            return;

        if (!_inputManager.IsKeyDown(Keyboard.Key.MouseLeft) || !_inputManager.IsKeyDown(Keyboard.Key.Alt))
            return;

        for (var current = control; current != null; current = current.Parent)
        {
            // A button goes on being a button.
            if (current is BaseButton)
                return;

            if (current is not BaseWindow window)
                continue;

            if (!HasChrome(window) || !window.IsInsideTree)
                return;

            _altDragWindow = window;
            _altDragOffset = PointerPosition(window) - window.Position;
            window.MoveToFront();
            return;
        }
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_altDragWindow is not { } window)
            return;

        if (!_inputManager.IsKeyDown(Keyboard.Key.MouseLeft) || window.Parent is not { } parent)
        {
            _altDragWindow = null;
            return;
        }

        var pointer = Vector2.Clamp(PointerPosition(window), Vector2.Zero, parent.Size);
        LayoutContainer.SetPosition(window, pointer - _altDragOffset);
    }

    private Vector2 PointerPosition(Control control)
    {
        return _inputManager.MouseScreenPosition.Position / control.UIScale;
    }
}
