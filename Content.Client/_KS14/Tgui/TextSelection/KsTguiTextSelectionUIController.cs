using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Lobby.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Tgui.TextSelection;

/// <summary>
///     tgui's text can be selected, as a browser's can, though not typed in. Mouse down on text in a tgui window, or on the chat
///         output, and drag; a double click takes a word, Ctrl+C copies, and a click anywhere else lets go. Button text stays
///         as it is, as tgui's <c>user-select: none</c> on a button.
/// </summary>
/// <remarks>
///     <para>
///         The engine selects only in <c>LineEdit</c> and <c>TextEdit</c>, so this does it from outside. It never takes an input event:
///         it hears of a click through <see cref="IUserInterfaceManager.OnKeyBindDown"/> and follows the mouse by polling, so every
///         click, drag and button press goes where it did. A <c>Label</c> ignores the mouse, so what is under the cursor is found by
///         walking the tree as the engine's own hit test does, with text counting as a hit.
///     </para>
///     <para>
///         The text's layout is worked out again by <see cref="KsTextLayouts"/>, once when a selection starts and again only when
///         the text, its width, the scale or its font change.
///     </para>
/// </remarks>
public sealed partial class KsTguiTextSelectionUIController : UIController
{
    private static readonly Type[] EngineDefaultTags =
    [
        typeof(BoldItalicTag),
        typeof(BoldTag),
        typeof(BulletTag),
        typeof(ColorTag),
        typeof(HeadingTag),
        typeof(ItalicTag),
    ];

    private const float DoubleClickSeconds = 0.4f;
    private const float DoubleClickDistance = 6f;

    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IClipboardManager _clipboardManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private MarkupTagManager _markupTagManager = default!;
    [Dependency] private IClyde _clyde = default!;

    private bool _textCursorShown;
    private bool _reapplyTextCursor;
    private Vector2 _lastMousePosition = new(float.NaN, float.NaN);
    private Control? _lastCursorOwner;

    private KsTextSelectionHighlight _highlight = default!;

    private Control? _target;
    private KsTextDocument? _document;
    private LayoutKey _key;
    private string[]? _entryTexts;
    private int _anchor;
    private int _caret;
    private bool _dragging;
    private bool _leftHeld;
    private bool _copyKeyHeld;

    private TimeSpan _lastDownTime;
    private Vector2 _lastDownPosition;
    private Control? _lastDownTarget;

    private readonly List<UIBox2> _boxes = new();

    /// <summary>
    ///     What the last copy put on the clipboard; for tests, which have no clipboard to read.
    /// </summary>
    public string? LastCopiedText { get; private set; }

    private bool Enabled => _configurationManager.GetCVar(KsCCVars.TguiStyle);

    /// <summary>
    ///     The control holding the selection, if any.
    /// </summary>
    public Control? SelectionTarget => HasSelection ? _target : null;

    public bool HasSelection => _target != null && _document != null && _anchor != _caret;

    /// <summary>
    ///     The selected text, or null while nothing is selected.
    /// </summary>
    public string? SelectedText => HasSelection ? _document!.TextBetween(_anchor, _caret) : null;

    /// <summary>
    ///     Everything a layout depends on, so that it is worked out again when, and only when, one of these changes.
    /// </summary>
    private readonly record struct LayoutKey(
        Vector2i PixelSize,
        float UIScale,
        Font? Font,
        string? Text,
        object? Content,
        int Count,
        int Align);

    public override void Initialize()
    {
        base.Initialize();

        _highlight = new KsTextSelectionHighlight();
        UIManager.PopupRoot.AddChild(_highlight);
        LayoutContainer.SetAnchorAndMarginPreset(_highlight, LayoutContainer.LayoutPreset.Wide);

        UIManager.OnKeyBindDown += OnKeyBindDown;
    }

    private void OnKeyBindDown(Control control)
    {
        if (!Enabled)
        {
            if (_target != null)
                ClearSelection();

            return;
        }

        // This fires for every key held over the interface. Only the press of the left button begins anything.
        if (!_inputManager.IsKeyDown(Keyboard.Key.MouseLeft) || _leftHeld)
            return;

        _leftHeld = true;

        // Alt-drag moves the window (KsTguiChromeUIController).
        if (_inputManager.IsKeyDown(Keyboard.Key.Alt))
        {
            ClearSelection();
            return;
        }

        BeginSelection(_inputManager.MouseScreenPosition);
    }

    /// <summary>
    ///     A press of the left button at a point: lets go of any selection, and starts one if the point is over text that can be
    ///         selected. A second press close to the first, on the same text, takes the word under it instead. Returns whether
    ///         the point was over such text.
    /// </summary>
    public bool BeginSelection(ScreenCoordinates position)
    {
        ClearSelection();

        if (!Enabled)
            return false;

        var hit = FindTextAt(position.Position);
        if (hit == null || !IsSelectable(hit))
        {
            _lastDownTarget = null;
            return false;
        }

        _target = hit;
        if (!Rebuild())
        {
            ClearSelection();
            return false;
        }

        var point = ToPage(position.Position);
        var caret = _document!.CaretAt(point);

        var now = _gameTiming.RealTime;
        var doubleClick = _lastDownTarget == hit
                          && (now - _lastDownTime).TotalSeconds <= DoubleClickSeconds
                          && Vector2.Distance(_lastDownPosition, position.Position) <= DoubleClickDistance;

        _lastDownTime = now;
        _lastDownPosition = position.Position;
        _lastDownTarget = hit;

        if (doubleClick && _document.GlyphAt(point) is var glyph and >= 0)
        {
            (_anchor, _caret) = _document.WordAt(glyph);
            _dragging = false;
        }
        else
        {
            _anchor = _caret = caret;
            _dragging = true;
        }

        UpdateHighlight();
        return true;
    }

    /// <summary>
    ///     Moves the far end of the selection to a point, as dragging the mouse does.
    /// </summary>
    public void DragTo(Vector2 screenPosition)
    {
        if (!_dragging || _target == null || _document == null)
            return;

        _caret = _document.CaretAt(ToPage(screenPosition));
    }

    public void EndDrag()
    {
        _dragging = false;
    }

    public void ClearSelection()
    {
        _target = null;
        _document = null;
        _entryTexts = null;
        _dragging = false;
        _anchor = _caret = 0;
        _boxes.Clear();
        _highlight.Rectangles.Clear();
        _highlight.Visible = false;
    }

    /// <summary>
    ///     Puts the selection on the clipboard. False while nothing is selected.
    /// </summary>
    public bool CopySelection()
    {
        if (SelectedText is not { Length: > 0 } text)
            return false;

        LastCopiedText = text;
        try
        {
            _clipboardManager.SetText(text);
        }
        catch (Exception exception)
        {
            // A headless client, or a platform with no clipboard, cannot copy; that is no reason to fail a frame.
            Log.Warning($"Could not copy the selected text: {exception.Message}");
        }

        return true;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var leftDown = _inputManager.IsKeyDown(Keyboard.Key.MouseLeft);
        if (!leftDown)
        {
            _leftHeld = false;
            _dragging = false;
        }

        var mousePosition = _inputManager.MouseScreenPosition.Position;
        var cursorOwner = UIManager.ControlFocused ?? UIManager.CurrentlyHovered;
        if (mousePosition != _lastMousePosition || cursorOwner != _lastCursorOwner || _reapplyTextCursor || _textCursorShown && !Enabled)
        {
            _lastMousePosition = mousePosition;
            UpdateCursor(mousePosition);
        }

        if (_target == null)
            return;

        if (!Enabled || !_target.VisibleInTree || !_target.IsInsideTree || !IsSelectable(_target))
        {
            ClearSelection();
            return;
        }

        if (!Refresh())
        {
            ClearSelection();
            return;
        }

        if (_dragging && leftDown)
            DragTo(_inputManager.MouseScreenPosition.Position);

        UpdateCopyKey();
        UpdateHighlight();
    }

    /// <summary>
    ///     Whether the text cursor (the I-beam) is shown: the mouse is over text that can be selected, or a selection is
    ///         being dragged out.
    /// </summary>
    public bool ShowingTextCursor => _textCursorShown;

    /// <summary>
    ///     Shows the I-beam over text that can be selected, as a browser does, and gives the cursor back to the control under
    ///         the mouse when it leaves. Text ignores the mouse, so the engine never asks it for a cursor: it takes the cursor
    ///         of the hovered control (or the one holding the mouse), and only when that one changes.
    /// </summary>
    public void UpdateCursor(Vector2 screenPosition)
    {
        var cursorOwner = UIManager.ControlFocused ?? UIManager.CurrentlyHovered;
        var ownerChanged = cursorOwner != _lastCursorOwner;
        _lastCursorOwner = cursorOwner;

        var wanted = Enabled && (_dragging && _target != null
                                 || FindTextAt(screenPosition) is { } hit && IsSelectable(hit));

        if (wanted)
        {
            // When the hovered control changes the engine puts its cursor up after the controllers have run, over this one,
            //      so the I-beam goes up again on the next frame as well.
            if (!_textCursorShown || ownerChanged || _reapplyTextCursor)
                _clyde.SetCursor(_clyde.GetStandardCursor(StandardCursorShape.IBeam));

            _reapplyTextCursor = ownerChanged;
        }
        else
        {
            _reapplyTextCursor = false;
            if (_textCursorShown)
                RestoreCursor(cursorOwner);
        }

        _textCursorShown = wanted;
    }

    /// <summary>
    ///     Has the engine put up the cursor it would have shown: setting a control's cursor to what it already is tells the engine
    ///         to work it out again.
    /// </summary>
    private void RestoreCursor(Control? cursorOwner)
    {
        if (cursorOwner == null)
        {
            UIManager.WorldCursor = UIManager.WorldCursor;
            return;
        }

        if (cursorOwner.CustomCursorShape is { } custom)
            cursorOwner.CustomCursorShape = custom;
        else
            cursorOwner.DefaultCursorShape = cursorOwner.DefaultCursorShape;
    }

    private void UpdateCopyKey()
    {
        var down = _inputManager.IsKeyDown(Keyboard.Key.Control) && _inputManager.IsKeyDown(Keyboard.Key.C);
        var pressed = down && !_copyKeyHeld;
        _copyKeyHeld = down;

        // A text box that has the keyboard copies its own selection.
        if (pressed && UIManager.KeyboardFocused is not (LineEdit or TextEdit))
            CopySelection();
    }

    private void UpdateHighlight()
    {
        _highlight.Rectangles.Clear();

        if (!HasSelection || _target == null || _document == null)
        {
            _highlight.Visible = false;
            return;
        }

        _boxes.Clear();
        _document.SelectionBoxes(_anchor, _caret, _boxes);

        var origin = PageOrigin(_target);
        var visible = VisibleRect(_target);

        foreach (var box in _boxes)
        {
            var screenBox = box.Translated(origin);
            var left = MathF.Max(screenBox.Left, visible.Left);
            var top = MathF.Max(screenBox.Top, visible.Top);
            var right = MathF.Min(screenBox.Right, visible.Right);
            var bottom = MathF.Min(screenBox.Bottom, visible.Bottom);

            if (right > left && bottom > top)
                _highlight.Rectangles.Add(new UIBox2(left, top, right, bottom));
        }

        _highlight.Visible = true;
    }

    /// <summary>
    ///     The part of the control the player can see: its box less whatever an ancestor clips.
    /// </summary>
    private static UIBox2 VisibleRect(Control control)
    {
        var rect = (UIBox2)control.GlobalPixelRect;
        for (var ancestor = control.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (!ancestor.RectClipContent)
                continue;

            var ancestorRect = ancestor.GlobalPixelRect;
            rect = new UIBox2(
                MathF.Max(rect.Left, ancestorRect.Left),
                MathF.Max(rect.Top, ancestorRect.Top),
                MathF.Min(rect.Right, ancestorRect.Right),
                MathF.Min(rect.Bottom, ancestorRect.Bottom));
        }

        return rect;
    }

    // Hit testing.

    /// <summary>
    ///     What the engine's hit test finds under a point, except that a <c>Label</c> or <c>RichTextLabel</c> counts as a hit
    ///         though it ignores the mouse. Walks the same way, topmost first, so a control in front of the text still wins.
    /// </summary>
    private Control? FindTextAt(Vector2 position)
    {
        return FindTextAt(UIManager.RootControl, position);
    }

    private static Control? FindTextAt(Control control, Vector2 position)
    {
        for (var i = control.ChildCount - 1; i >= 0; i--)
        {
            var child = control.GetChild(i);
            if (!child.Visible || child.RectClipContent && !child.PixelRect.Contains((Vector2i)position))
                continue;

            if (FindTextAt(child, position - child.PixelPosition) is { } found)
                return found;
        }

        if (control is Label or RichTextLabel
            && position.X >= 0f && position.Y >= 0f
            && position.X < control.PixelSize.X && position.Y < control.PixelSize.Y)
        {
            return control;
        }

        // HasPoint is protected; a control that overrides it is rare enough that its box stands in.
        if (control.MouseFilter != Control.MouseFilterMode.Ignore
            && position.X >= 0f && position.Y >= 0f
            && position.X < control.PixelSize.X && position.Y < control.PixelSize.Y)
        {
            return control;
        }

        return null;
    }

    /// <summary>
    ///     Whether a control is text that belongs to a tgui window or to the chat, and is not button text.
    /// </summary>
    public bool IsSelectable(Control control)
    {
        if (control is not (Label or RichTextLabel or OutputPanel))
            return false;

        // A link inside rich text is a label of its own, which takes clicks; its text is part of the message's.
        if (control.Parent is RichTextLabel or OutputPanel)
            return false;

        var inTguiWindow = false;
        var inChat = false;
        for (var ancestor = control; ancestor != null; ancestor = ancestor.Parent)
        {
            // Button text is not selectable, nor is anything that takes the mouse itself.
            if (ancestor is BaseButton or LineEdit or TextEdit or Slider or ScrollBar)
                return false;

            // A drag on the title bar moves the window.
            if (ancestor.HasStyleClass(DefaultWindow.StyleClassWindowHeader))
                return false;

            if (ancestor is LobbyGui)
                return false;

            if (ancestor is ChatBox)
                inChat = true;

            if (ancestor is BaseWindow window)
            {
                if (ancestor.GetType().Name.StartsWith("KsNuke", StringComparison.Ordinal))
                    return false;

                inTguiWindow = KsTguiChromeUIController.HasChrome(window);
            }
        }

        return control is OutputPanel ? inChat || inTguiWindow : inTguiWindow;
    }

    // Layout.

    /// <summary>
    ///     Makes the document if there is none, or if the control's text or size has changed since.
    /// </summary>
    private bool Refresh()
    {
        if (_target == null)
            return false;

        if (_document != null && ComputeKey(_target) == _key)
            return true;

        // A message list moves under a selection as lines arrive and the oldest go; keep the selection on the same lines.
        var oldDocument = _document;
        var oldTexts = _entryTexts;
        var oldAnchor = _anchor;
        var oldCaret = _caret;

        if (!Rebuild())
            return false;

        if (oldDocument == null || (oldAnchor == oldCaret))
            return true;

        if (_target is OutputPanel && oldTexts != null && _entryTexts != null)
        {
            var shift = FindShift(oldTexts, _entryTexts);
            if (shift < 0)
                return false;

            _anchor = Remap(oldDocument, _document!, oldAnchor, shift);
            _caret = Remap(oldDocument, _document!, oldCaret, shift);
            return _anchor >= 0 && _caret >= 0;
        }

        // Anything else that changed is different text.
        return false;
    }

    private static int FindShift(string[] oldTexts, string[] newTexts)
    {
        if (oldTexts.Length == 0)
            return -1;

        for (var shift = 0; shift + oldTexts.Length <= newTexts.Length; shift++)
        {
            if (newTexts[shift] == oldTexts[0] && newTexts[shift + oldTexts.Length - 1] == oldTexts[^1])
                return shift;
        }

        return -1;
    }

    private static int Remap(KsTextDocument oldDocument, KsTextDocument newDocument, int caret, int shift)
    {
        for (var i = 0; i < oldDocument.Parts.Count; i++)
        {
            var part = oldDocument.Parts[i];
            if (caret > part.FirstGlyph + part.Layout.Glyphs.Count)
                continue;

            var newPart = newDocument.Parts[i + shift];
            return newPart.FirstGlyph + (caret - part.FirstGlyph);
        }

        return -1;
    }

    private bool Rebuild()
    {
        if (_target == null)
            return false;

        var document = new KsTextDocument();
        string[]? entryTexts = null;

        switch (_target)
        {
            case Label label:
            {
                if (KsTextLayouts.ForLabel(label, GetLabelFont(label)) is not { } layout)
                    return false;

                document.Add(layout, Vector2.Zero);
                break;
            }
            case RichTextLabel rich:
            {
                if (BuildRichTextLabel(rich) is not { } layout)
                    return false;

                document.Add(layout, Vector2.Zero);
                break;
            }
            case OutputPanel panel:
                entryTexts = BuildOutputPanel(panel, document);
                break;
            default:
                return false;
        }

        _document = document;
        _entryTexts = entryTexts;
        _key = ComputeKey(_target);
        return true;
    }

    private LayoutKey ComputeKey(Control control)
    {
        switch (control)
        {
            case Label label:
            {
                string? text = null;
                try
                {
                    text = label.Text;
                }
                catch (InvalidOperationException)
                {
                }

                return new LayoutKey(label.PixelSize, label.UIScale, GetLabelFont(label), text, null, 0, (int)label.Align * 8 + (int)label.VAlign);
            }
            case RichTextLabel rich:
                return new LayoutKey(rich.PixelSize, rich.UIScale, GetRichFont(rich), null, rich.Nodes, rich.Nodes.Count, 0);
            case OutputPanel panel:
                return new LayoutKey(panel.PixelSize, panel.UIScale, GetPanelFont(panel), null, null, panel.EntryCount, 0);
            default:
                return default;
        }
    }

    private Font GetLabelFont(Label label)
    {
        if (label.FontOverride != null)
            return label.FontOverride;

        return label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var font)
            ? font
            : UIManager.ThemeDefaults.LabelFont;
    }

    private Font GetRichFont(Control control)
    {
        return control.TryGetStyleProperty<Font>("font", out var font) ? font : UIManager.ThemeDefaults.DefaultFont;
    }

    private Font GetPanelFont(OutputPanel panel)
    {
        return GetRichFont(panel);
    }

    private KsTextLayout? BuildRichTextLabel(RichTextLabel rich)
    {
        var message = rich.GetFormattedMessage();
        if (message == null)
            return null;

        var font = GetRichFont(rich);
        var controls = new List<Control>(rich.Controls);
        var scale = rich.UIScale;
        var width = (float)rich.PixelSize.X;
        var lineHeightScale = rich.LineHeightScale;

        // Which tags the label allows is not readable, but it decides what text some tags add and which fonts they use. Take
        //      the reading that lands on the size the label measured.
        var desiredHeight = (int)MathF.Round(rich.DesiredSize.Y * scale);
        KsTextLayout? first = null;
        foreach (var tags in new[] { (Type[]?)null, EngineDefaultTags })
        {
            var layout = KsTextLayouts.Rich(message, rich, _markupTagManager, tags, font, width, scale, lineHeightScale, controls);
            first ??= layout;
            if (layout.Height == desiredHeight)
                return layout;
        }

        return first;
    }

    /// <summary>
    ///     Lays out every message of the panel down one page, as <c>OutputPanel.Draw</c> does.
    /// </summary>
    private string[] BuildOutputPanel(OutputPanel panel, KsTextDocument document)
    {
        var font = GetPanelFont(panel);
        var scale = panel.UIScale;
        var separation = font.GetLineSeparation(scale);
        var width = GetPanelContentBox(panel).Width;

        var count = panel.EntryCount;
        var texts = new string[count];
        var y = 0f;
        for (var i = 0; i < count; i++)
        {
            var message = panel.GetMessage(i);
            var layout = KsTextLayouts.Rich(message, panel, _markupTagManager, null, font, width, scale);
            document.Add(layout, new Vector2(0f, y));
            texts[i] = layout.Text;
            y += layout.Height + separation;
        }

        return texts;
    }

    private static UIBox2 GetPanelContentBox(OutputPanel panel)
    {
        StyleBox? style = panel.StyleBoxOverride;
        if (style == null)
            panel.TryGetStyleProperty<StyleBox>(OutputPanel.StylePropertyStyleBox, out style);

        var box = style?.GetContentBox(panel.PixelSizeBox, panel.UIScale) ?? (UIBox2)panel.PixelSizeBox;

        foreach (var child in panel.Children)
        {
            if (child is VScrollBar scrollBar)
            {
                box.Right = MathF.Max(box.Left, box.Right - scrollBar.DesiredPixelSize.X);
                break;
            }
        }

        return box;
    }

    // Mapping the page to the screen.

    /// <summary>
    ///     Where the top left of the document is, in screen pixels.
    /// </summary>
    private Vector2 PageOrigin(Control control)
    {
        var origin = (Vector2)control.GlobalPixelPosition;
        if (control is not OutputPanel panel)
            return origin;

        var contentBox = GetPanelContentBox(panel);
        var scroll = 0f;
        foreach (var child in panel.Children)
        {
            if (child is VScrollBar scrollBar)
            {
                scroll = scrollBar.Value;
                break;
            }
        }

        return origin + new Vector2(contentBox.Left, contentBox.Top - scroll);
    }

    private Vector2 ToPage(Vector2 screenPosition)
    {
        return _target == null ? screenPosition : screenPosition - PageOrigin(_target);
    }
}
