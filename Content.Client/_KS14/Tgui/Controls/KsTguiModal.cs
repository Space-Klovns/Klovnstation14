using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's Modal: a <see cref="KsTguiDimmer"/> with a panel on <c>--color-base</c>, padded 12, in the middle. It
///         lives inside the window (add it to a host that stacks, such as <see cref="KsTguiDimmer.Over"/> makes), takes the
///         keyboard focus when opened, and closes on Escape. It is hidden until <see cref="Open"/>.
/// </summary>
/// <remarks>
///     A text box inside takes the first Escape to release its focus, as it does anywhere; the second closes the modal.
/// </remarks>
[Virtual]
public class KsTguiModal : KsTguiDimmer
{
    /// <summary>
    ///     <c>--modal-padding</c>, <c>--space-xl</c>.
    /// </summary>
    private const float Padding = 12f;

    private readonly PanelContainer _panel;
    private readonly StyleBoxFlat _panelBox = new() { BackgroundColor = KsTguiPalettes.Base };

    public event Action? Closed;

    public KsTguiModal()
    {
        Visible = false;
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;

        _panel = new PanelContainer
        {
            PanelOverride = _panelBox,
            Margin = new Thickness(12f),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };
        AddChild(_panel);
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        _panelBox.BackgroundColor = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyWindowBase, out var windowBase) ? windowBase : KsTguiPalettes.Base;
    }

    /// <summary>
    ///     The modal's own contents; the panel hugs them, inside its padding.
    /// </summary>
    public Control? Body
    {
        get => _panel.ChildCount > 0 ? _panel.GetChild(0) : null;
        set
        {
            _panel.RemoveAllChildren();
            if (value == null)
                return;

            value.Margin = new Thickness(Padding);
            _panel.AddChild(value);
        }
    }

    public bool IsOpen => Visible;

    public void Open()
    {
        Visible = true;
        GrabKeyboardFocus();
    }

    public void Close()
    {
        if (!Visible)
            return;

        Visible = false;
        ReleaseKeyboardFocus();
        Closed?.Invoke();
    }

    /// <summary>
    ///     Escape closes. True when the key was one it handles.
    /// </summary>
    public bool HandleKeyFunction(BoundKeyFunction function)
    {
        if (!Visible || function != EngineKeyFunctions.TextReleaseFocus && function != EngineKeyFunctions.EscapeMenu)
            return false;

        Close();
        return true;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (HandleKeyFunction(args.Function))
            args.Handle();
    }
}
