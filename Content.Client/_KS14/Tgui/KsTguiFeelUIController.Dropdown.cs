using System.Linq;
using Content.Client._KS14.Tgui.Backdrop;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     What tgui's Dropdown does around opening that a stylesheet can't: the menu easing in 6px below its button, the chevron
///         turning over while it is open, and the scrollbar arrows' clicks.
/// </summary>
public sealed partial class KsTguiFeelUIController
{
    /// <summary>
    ///     Floating's <c>contentOffset</c>, and <c>--dropdown-menu-max-height</c> with the wrapper's border either side.
    /// </summary>
    public const float DropdownMenuGap = 6f;
    public const float DropdownMenuMaxHeight = 200f;
    private const float DropdownMenuBorder = 1f;
    private const float DropdownMenuPadding = 4f;

    /// <summary>
    ///     <c>--floating-transition-time</c> is <c>--transition-time-medium</c>, and the menu starts at <c>scale(0.9)</c>.
    /// </summary>
    private const float DropdownOpenScale = 0.9f;

    /// <summary>
    ///     The chevron's <c>transition: transform var(--dropdown-transition)</c>.
    /// </summary>
    private const float ChevronTurnSeconds = 0.2f;

    private const string ChevronShader = "KsTguiGlyphDropdownCell";

    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IInputManager _inputManager = default!;

    private readonly KsTguiScrollArrows _scrollArrows = new();
    private readonly List<DropdownOpening> _dropdownOpenings = new();
    private readonly List<ChevronTurn> _chevronTurns = new();
    private readonly List<KsTguiDropdownGhost> _dropdownGhosts = new();

    /// <summary>
    ///     The popups whose showing is already watched, to place the menu in the very call that shows it.
    /// </summary>
    private static readonly AttachedProperty<bool> PlacementHookedProperty =
        AttachedProperty<bool>.Create("KsTguiDropdownPlacementHooked", typeof(KsTguiFeelUIController));

    /// <summary>
    ///     The boxes of an open popup whose new children are tagged as entries, so an option button that is cleared and filled
    ///         again while open keeps the dropdown look.
    /// </summary>
    private readonly Dictionary<Popup, List<Control>> _entryBoxes = new();

    private sealed class DropdownOpening(Popup popup, KsTguiScaleHost host, Control? anchor)
    {
        public readonly Popup Popup = popup;
        public readonly KsTguiScaleHost Host = host;
        public readonly Control? Anchor = anchor;
        public float Elapsed;
    }

    private sealed class ChevronTurn(TextureRect triangle, ShaderInstance shader, Popup popup)
    {
        public readonly TextureRect Triangle = triangle;
        public readonly ShaderInstance Shader = shader;
        public Popup Popup = popup;

        /// <summary>
        ///     0 closed, 1 open.
        /// </summary>
        public float Progress;
    }

    /// <summary>
    ///     Called for every popup an option button opens, once the theme is known.
    /// </summary>
    private void BeginDropdown(Control control)
    {
        if (control is not Popup popup)
            return;

        // An option button reuses its popup, so on the second open the contents are already in a host.
        var host = popup.Children.OfType<KsTguiScaleHost>().FirstOrDefault();
        var container = host ?? (Control)popup;
        if (!IsTguiDropdown(container))
        {
            // Left as it stood when it was closed, which may have been part way through.
            if (host != null)
            {
                host.Scale = 1f;
                host.Modulate = Color.White;
                host.Backdrop = null;
            }

            return;
        }

        // Opened again before its closing transition ended: that is over.
        DropDropdownGhostOf(popup);

        var button = FindOptionButton(UIManager.CurrentlyHovered);
        Control? anchor = (popup as IKsTguiDropdownAnchored)?.DropdownAnchor ?? button;

        // The menu's rows sit 4px in from a 1px border, and scroll with it. The engine measures the menu before it has a
        //      style, so the popup is capped as well as its scroll container, or the engine's stale size would win.
        ScrollContainer? scroll = null;
        foreach (var child in container.Children)
        {
            if (child is ScrollContainer found)
                scroll = found;
        }

        if (scroll == null)
            return;

        var capped = DropdownMenuMaxHeight + DropdownMenuBorder * 2f;
        popup.MaxHeight = capped;
        scroll.MaxHeight = capped;

        foreach (var child in scroll.Children)
        {
            if (child is not ScrollBar)
                child.Margin = new Thickness(DropdownMenuPadding + DropdownMenuBorder);
        }

        if (host == null)
        {
            // Between the popup and what it holds, to scale it.
            host = new KsTguiScaleHost();
            foreach (var child in popup.Children.ToArray())
            {
                popup.RemoveChild(child);
                host.AddChild(child);
            }

            popup.AddChild(host);
        }

        host.Scale = DropdownOpenScale;
        host.Modulate = Color.White.WithAlpha(0f);
        host.Backdrop = UIManager.GetUIController<KsTguiBackdropUIController>();
        _dropdownOpenings.RemoveAll(found => found.Popup == popup);
        _dropdownOpenings.Add(new DropdownOpening(popup, host, anchor));

        if (button != null)
            TurnChevron(button, popup);
    }

    /// <summary>
    ///     Called when the engine takes a popup out of the modal layer. A tgui dropdown's menu goes on being drawn for tgui's
    ///         closing transition.
    /// </summary>
    private void OnModalRootChildRemoved(Control control)
    {
        if (control is Popup closing)
            UnwatchDropdownEntries(closing);

        if (control is not Popup popup || popup.Parent != null || !_configurationManager.GetCVar(KsCCVars.TguiStyle))
            return;

        if (popup.Children.OfType<KsTguiScaleHost>().FirstOrDefault() is not { } host || host.Modulate.A <= 0.01f)
            return;

        DropDropdownGhostOf(popup);

        // Virtual pixels: the popup has left the tree, so it no longer knows the UI scale, but its position is still right.
        var position = UIManager.ModalRoot.GlobalPosition + popup.Position;
        var ghost = new KsTguiDropdownGhost(popup, host, position);
        popup.RemoveChild(host);
        ghost.AddChild(host);
        UIManager.PopupRoot.AddChild(ghost);
        _dropdownGhosts.Add(ghost);
    }

    /// <summary>
    ///     The ghosts of the menus closing, for tests.
    /// </summary>
    public IReadOnlyList<KsTguiDropdownGhost> DropdownGhosts => _dropdownGhosts;

    private void DropDropdownGhostOf(Popup popup)
    {
        for (var i = _dropdownGhosts.Count - 1; i >= 0; i--)
        {
            if (_dropdownGhosts[i].Popup == popup)
                DropDropdownGhost(i, popupIsClosed: popup.Parent == null);
        }
    }

    /// <param name="popupIsClosed">
    ///     False when the popup is open again, whose host is then the open menu's and is left as it is.
    /// </param>
    private void DropDropdownGhost(int index, bool popupIsClosed)
    {
        var ghost = _dropdownGhosts[index];
        _dropdownGhosts.RemoveAt(index);

        // The host goes home first: orphaning the ghost would take it along.
        ghost.ReleaseHost();
        ghost.Orphan();

        if (!popupIsClosed)
            return;

        // The popup waits for its next open, which sets the host up again; under Baron it is opened as it stands.
        ghost.Host.Scale = 1f;
        ghost.Host.Modulate = Color.White;
        ghost.Host.ReleaseBackdrop();
    }

    private static bool IsTguiDropdown(Control popup)
    {
        foreach (var child in popup.Children)
        {
            // A control just added has not been styled yet.
            if (child is PanelContainer panel
                && panel.HasStyleClass(OptionButton.StyleClassOptionsBackground)
                && RunStyle(panel)
                && panel.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out var box)
                && box is KsTguiStyleBox)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RunStyle(Control control)
    {
        control.ForceRunStyleUpdate();
        return true;
    }

    private static OptionButton? FindOptionButton(Control? control)
    {
        for (var current = control; current != null; current = current.Parent)
        {
            if (current is OptionButton button)
                return button;
        }

        return null;
    }

    private void TurnChevron(OptionButton button, Popup popup)
    {
        var triangle = FindTriangle(button);
        if (triangle == null)
            return;

        foreach (var turn in _chevronTurns)
        {
            if (turn.Triangle == triangle)
            {
                turn.Popup = popup;
                return;
            }
        }

        var shader = _prototypeManager.Index<ShaderPrototype>(ChevronShader).InstanceUnique();
        triangle.ShaderOverride = shader;
        _chevronTurns.Add(new ChevronTurn(triangle, shader, popup));
    }

    private static TextureRect? FindTriangle(Control control)
    {
        foreach (var child in control.Children)
        {
            if (child is TextureRect rect && rect.HasStyleClass(OptionButton.StyleClassOptionTriangle))
                return rect;

            if (FindTriangle(child) is { } found)
                return found;
        }

        return null;
    }

    private void UpdateDropdowns(float deltaSeconds)
    {
        for (var i = _dropdownOpenings.Count - 1; i >= 0; i--)
        {
            var opening = _dropdownOpenings[i];
            if (!opening.Popup.IsInsideTree)
            {
                _dropdownOpenings.RemoveAt(i);
                continue;
            }

            opening.Elapsed += deltaSeconds;
            var progress = MathF.Min(opening.Elapsed / FadeSeconds, 1f);
            var eased = CssEase(progress);
            opening.Host.Scale = DropdownOpenScale + (1f - DropdownOpenScale) * eased;
            opening.Host.Modulate = Color.White.WithAlpha(eased);

            if (progress >= 1f)
                _dropdownOpenings.RemoveAt(i);
        }

        for (var i = _dropdownGhosts.Count - 1; i >= 0; i--)
        {
            var ghost = _dropdownGhosts[i];

            // The popup is open again, or the popup layer went.
            if (ghost.Popup.Parent != null || ghost.Parent == null)
            {
                DropDropdownGhost(i, popupIsClosed: ghost.Popup.Parent == null);
                continue;
            }

            ghost.Advance(deltaSeconds);
            if (ghost.Finished)
                DropDropdownGhost(i, popupIsClosed: true);
        }

        for (var i = _chevronTurns.Count - 1; i >= 0; i--)
        {
            var turn = _chevronTurns[i];
            var open = turn.Popup.IsInsideTree && turn.Popup.Visible;
            var step = deltaSeconds / ChevronTurnSeconds;
            turn.Progress = Math.Clamp(turn.Progress + (open ? step : -step), 0f, 1f);

            if (!turn.Triangle.IsInsideTree || (!open && turn.Progress <= 0f))
            {
                if (turn.Triangle.ShaderOverride == turn.Shader)
                    turn.Triangle.ShaderOverride = null;

                _chevronTurns.RemoveAt(i);
                continue;
            }

            turn.Shader.SetParameter("angle", MathF.PI * Ease(turn.Progress));
        }
    }

    /// <summary>
    ///     Places the menu beside what it opens from, as Floating does: below, or above when it doesn't fit below, and only
    ///         pushed against the screen's edge when it fits neither way.
    /// </summary>
    private void PlaceDropdown(DropdownOpening opening)
    {
        var popup = opening.Popup;
        var modalRoot = UIManager.ModalRoot;
        var origin = PopupContainer.GetPopupOrigin(popup);

        if (opening.Anchor is not { } anchor)
        {
            origin.Y += DropdownMenuGap - 1f;
            opening.Host.PivotBottom = false;
            PopupContainer.SetPopupOrigin(popup, origin);
            return;
        }

        // The size the engine's layout will give it, worked out now so that the menu is right on its first drawn frame.
        popup.Measure(modalRoot.Size);
        var height = popup.DesiredSize.Y;
        var top = anchor.GlobalPosition.Y - modalRoot.GlobalPosition.Y;
        var below = top + anchor.Size.Y + DropdownMenuGap;
        var above = top - DropdownMenuGap - height;
        var flip = below + height > modalRoot.Size.Y && above >= 0f;

        origin.Y = flip ? above : below;
        opening.Host.PivotBottom = flip;
        PopupContainer.SetPopupOrigin(popup, origin);
    }

    /// <summary>
    ///     A popup showing is the engine's last step in opening it, after it has set where it goes. Placing the menu then means
    ///         it is final before any layout or drawing.
    /// </summary>
    private void OnDropdownVisibilityChanged(Control control)
    {
        if (control is not Popup { Visible: true } popup)
            return;

        foreach (var opening in _dropdownOpenings)
        {
            if (opening.Popup == popup)
            {
                PlaceDropdown(opening);
                return;
            }
        }
    }

    /// <summary>
    ///     Follows the entries a dropdown gains while it is open, and its showing.
    /// </summary>
    private void WatchDropdown(Popup popup)
    {
        if (!popup.GetValue(PlacementHookedProperty))
        {
            popup.SetValue(PlacementHookedProperty, true);
            popup.OnVisibilityChanged += OnDropdownVisibilityChanged;
        }

        UnwatchDropdownEntries(popup);

        var boxes = new List<Control>();
        CollectBoxes(popup, boxes);
        foreach (var box in boxes)
            box.OnChildAdded += OnDropdownEntryAdded;

        _entryBoxes[popup] = boxes;
    }

    private void UnwatchDropdownEntries(Popup popup)
    {
        if (!_entryBoxes.Remove(popup, out var boxes))
            return;

        foreach (var box in boxes)
            box.OnChildAdded -= OnDropdownEntryAdded;
    }

    private static void CollectBoxes(Control control, List<Control> boxes)
    {
        foreach (var child in control.Children)
        {
            if (child is BoxContainer)
                boxes.Add(child);

            CollectBoxes(child, boxes);
        }
    }

    private static void OnDropdownEntryAdded(Control child)
    {
        if (child is Button button)
            button.AddStyleClass(KsTguiSheetlet.StyleClassDropdownEntry);
        else
            TagDropdownEntries(child);
    }

    private void UpdateScrollArrows(float deltaSeconds)
    {
        _scrollArrows.Update(
            UIManager.CurrentlyHovered,
            _inputManager.IsKeyDown(Keyboard.Key.MouseLeft),
            UIManager.MousePositionScaled.Position,
            deltaSeconds);
    }

    /// <summary>
    ///     Lets go of every dropdown effect in motion, for a theme change.
    /// </summary>
    private void DropAllDropdownEffects()
    {
        foreach (var turn in _chevronTurns)
        {
            if (turn.Triangle.ShaderOverride == turn.Shader)
                turn.Triangle.ShaderOverride = null;
        }

        _chevronTurns.Clear();
        _dropdownOpenings.Clear();

        for (var i = _dropdownGhosts.Count - 1; i >= 0; i--)
        {
            _dropdownGhosts[i].Host.Backdrop = null;
            DropDropdownGhost(i, popupIsClosed: _dropdownGhosts[i].Popup.Parent == null);
        }
    }
}

/// <summary>
///     A popup that opens as a tgui dropdown beside a control that is not an <see cref="OptionButton"/>.
/// </summary>
public interface IKsTguiDropdownAnchored
{
    /// <summary>
    ///     The control the menu opens below, or above where there is no room.
    /// </summary>
    Control? DropdownAnchor { get; }
}
