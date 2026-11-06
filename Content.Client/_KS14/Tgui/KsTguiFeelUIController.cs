using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Chat;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     What tgui's CSS transitions do and a stylesheet can't, since rules switch instantly: buttons easing between states,
///         the input border lighting up on focus, tooltips fading in.
/// </summary>
/// <remarks>
///     The engine has no hover or focus event, so <see cref="FrameUpdate"/> compares references each frame. That is the
///         whole cost until something changes: only buttons in motion are touched after that.
/// </remarks>
public sealed partial class KsTguiFeelUIController : UIController
{
    /// <summary>
    ///     tgui's <c>--transition-time-medium</c>, used by its buttons and tooltips.
    /// </summary>
    private const float FadeSeconds = 0.2f;

    /// <summary>
    ///     tgui-core's <c>--adjust-active</c>, taken off a held button, as lightness points.
    /// </summary>
    private const float HeldLightness = -0.075f;

    /// <summary>
    ///     tgui-core's <c>--adjust-hover</c>, added to a hovered button.
    /// </summary>
    private const float HoverLightness = 0.1f;

    private sealed class Fade(BaseButton button, Color start)
    {
        public readonly BaseButton Button = button;
        public Color Start = start;
        public Color Current = start;
        public Color Target = start;
        public float Elapsed;
    }

    private sealed class TooltipFade(Control control)
    {
        public readonly Control Control = control;
        public float Elapsed;
    }

    [Dependency] private IConfigurationManager _configurationManager = default!;

    private readonly List<Fade> _fades = new();
    private readonly List<TooltipFade> _tooltipFades = new();

    private Control? _lastHovered;
    private LineEdit? _focusedInput;

    public override void Initialize()
    {
        base.Initialize();

        UIManager.PopupRoot.OnChildAdded += OnPopupRootChildAdded;
        UIManager.ModalRoot.OnChildAdded += OnModalRootChildAdded;
    }

    /// <summary>
    ///     Lets go of everything in motion, since it was all worked out from the old theme's colours.
    /// </summary>
    public void ThemeChanged()
    {
        foreach (var fade in _fades)
            fade.Button.ModulateSelfOverride = null;

        _fades.Clear();
        _lastHovered = null;

        _focusedInput?.RemoveStyleClass(KsTguiSheetlet.StyleClassInputFocused);
        _focusedInput = null;

        UIManager.GetUIController<ChatUIController>().KsRefreshChatWindow();
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        UpdateButtons(UIManager.CurrentlyHovered, args.DeltaSeconds);
        UpdateInputFocus();
        UpdateTooltips(args.DeltaSeconds);
    }

    /// <summary>
    ///     Advances every button in motion. Takes the hovered control as an argument so that a test can supply it: the UI
    ///         manager replaces any hover with the real mouse's each frame, and a headless client has no mouse.
    /// </summary>
    public void UpdateButtons(Control? hovered, float deltaSeconds)
    {
        _lastHovered = hovered;

        // Every frame and not only when the hover moves: a button re-enabled under a still mouse starts hovering without
        //      the hover changing. StartFade ignores a button already fading.
        if (FindFadingButton(hovered) is { } button)
            StartFade(button);

        UpdateFades(deltaSeconds);
    }

    /// <summary>
    ///     The button a hover lands on, from a control that may be nested in it.
    /// </summary>
    private static BaseButton? FindFadingButton(Control? control)
    {
        for (var current = control; current != null; current = current.Parent)
        {
            if (current is BaseButton button)
                return CanFade(button) ? button : null;
        }

        return null;
    }

    /// <summary>
    ///     Only buttons a tgui rule styled. That leaves out the whole UI under Baron, the lobby's, and the Nuke's keys. An
    ///         override of anyone else's belongs to them.
    /// </summary>
    private static bool CanFade(BaseButton button)
    {
        // tgui's disabled button has no hover state at all.
        if (button.Disabled)
            return false;

        if (!button.TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyNormalColor, out _))
            return false;

        if (button.ModulateSelfOverride != null)
            return false;

        return button is not ContainerButton { StyleBoxOverride: not null } || button is ListContainerButton;
    }

    private void StartFade(BaseButton button)
    {
        foreach (var fade in _fades)
        {
            if (fade.Button == button)
                return;
        }

        // By now the sheet has already moved the button to its hover colour, hence a property that keeps the normal one.
        var start = button.DrawMode == BaseButton.DrawModeEnum.Pressed
            ? button.ActualModulateSelf
            : button.StylePropertyDefault(KsTguiSheetlet.StylePropertyNormalColor, Color.White);

        // In this same frame, before anything draws, so the hover colour never gets shown first.
        button.ModulateSelfOverride = start;
        _fades.Add(new Fade(button, start));
    }

    private void UpdateFades(float deltaSeconds)
    {
        for (var i = _fades.Count - 1; i >= 0; i--)
        {
            var fade = _fades[i];
            var button = fade.Button;

            if (!button.IsInsideTree || button.Disabled || button.ModulateSelfOverride != fade.Current)
            {
                if (button.ModulateSelfOverride == fade.Current)
                    button.ModulateSelfOverride = null;

                _fades.RemoveAt(i);
                continue;
            }

            var target = TargetColor(button, out var instant);
            if (target != fade.Target)
            {
                fade.Start = instant ? target : fade.Current;
                fade.Target = target;
                fade.Elapsed = 0f;
            }

            fade.Elapsed += deltaSeconds;
            var progress = MathF.Min(fade.Elapsed / FadeSeconds, 1f);
            fade.Current = Color.InterpolateBetween(fade.Start, fade.Target, Ease(progress));
            button.ModulateSelfOverride = fade.Current;

            if (progress >= 1f && !button.IsHovered && button.DrawMode != BaseButton.DrawModeEnum.Pressed)
            {
                button.ModulateSelfOverride = null;
                _fades.RemoveAt(i);
            }
        }
    }

    /// <summary>
    ///     The colour a button should be heading for. Mostly the sheet's, with the two states the engine's pseudo-classes
    ///         run together told apart: a held button and a selected one are both just "pressed".
    /// </summary>
    private static Color TargetColor(BaseButton button, out bool instant)
    {
        instant = false;

        if (!button.TryGetStyleProperty<Color>(Control.StylePropertyModulateSelf, out var color))
            color = Color.White;

        if (button.DrawMode != BaseButton.DrawModeEnum.Pressed)
            return color;

        if (button.ToggleMode && button.Pressed)
            return button.IsHovered ? ShiftLightness(color, HoverLightness) : color;

        // tgui has no transition on :active. A held button changes at once.
        instant = true;
        var normal = button.StylePropertyDefault(KsTguiSheetlet.StylePropertyNormalColor, color);
        return normal.A < 1f ? color : ShiftLightness(normal, HeldLightness);
    }

    /// <summary>
    ///     tgui adjusts colours by absolute lightness points, keeping hue and saturation. Translucent washes aren't adjusted.
    /// </summary>
    public static Color ShiftLightness(Color color, float shift)
    {
        if (color.A < 1f)
            return color;

        var hsl = Color.ToHsl(color);
        hsl.Z = Math.Clamp(hsl.Z + shift, 0f, 1f);
        return Color.FromHsl(hsl);
    }

    /// <summary>
    ///     Close to CSS's <c>ease</c>: quick to start, gentle to finish.
    /// </summary>
    private static float Ease(float progress)
    {
        var remaining = 1f - progress;
        return 1f - remaining * remaining * remaining;
    }

    private void UpdateInputFocus()
    {
        var focused = UIManager.KeyboardFocused as LineEdit;
        if (focused == _focusedInput)
            return;

        _focusedInput?.RemoveStyleClass(KsTguiSheetlet.StyleClassInputFocused);
        _focusedInput = null;

        // Not chat's, whose input has no box to light up.
        if (focused != null
            && focused.TryGetStyleProperty<StyleBox>(LineEdit.StylePropertyStyleBox, out var box)
            && box is KsTguiStyleBox)
        {
            focused.AddStyleClass(KsTguiSheetlet.StyleClassInputFocused);
            _focusedInput = focused;
        }
    }

    private void OnPopupRootChildAdded(Control control)
    {
        if (!_configurationManager.GetCVar(KsCCVars.TguiStyle))
            return;

        // A tooltip of a control in a themed window wears the window's theme: the popup layer is outside the window.
        if (KsTguiWindowThemes.FindThemedSheet(UIManager.CurrentlyHovered) is { } themedSheet)
            control.Stylesheet = themedSheet;

        if (control is not Tooltip && !control.HasStyleClass(StyleClass.TooltipPanel))
            return;

        control.Modulate = Color.White.WithAlpha(0f);
        _tooltipFades.Add(new TooltipFade(control));
    }

    private void UpdateTooltips(float deltaSeconds)
    {
        for (var i = _tooltipFades.Count - 1; i >= 0; i--)
        {
            var fade = _tooltipFades[i];
            fade.Elapsed += deltaSeconds;

            var progress = MathF.Min(fade.Elapsed / FadeSeconds, 1f);
            fade.Control.Modulate = Color.White.WithAlpha(Ease(progress));

            if (progress >= 1f || !fade.Control.IsInsideTree)
                _tooltipFades.RemoveAt(i);
        }
    }

    /// <summary>
    ///     Tags an <see cref="OptionButton"/>'s entries as it opens. They are plain buttons in an unclassed box, so no rule
    ///         can select them through their parents, and the option button adds its entries' classes for itself.
    /// </summary>
    private void OnModalRootChildAdded(Control control)
    {
        if (!control.HasStyleClass(OptionButton.StyleClassPopup))
            return;

        TagDropdownEntries(control);

        // A dropdown of a themed window wears its theme: the modal layer is outside the window. The option button is under
        //      the mouse when it opens.
        if (KsTguiWindowThemes.FindThemedSheet(UIManager.CurrentlyHovered) is { } themedSheet)
            control.Stylesheet = themedSheet;
    }

    private static void TagDropdownEntries(Control control)
    {
        foreach (var child in control.Children)
        {
            if (child is Button button)
                button.AddStyleClass(KsTguiSheetlet.StyleClassDropdownEntry);
            else
                TagDropdownEntries(child);
        }
    }
}
