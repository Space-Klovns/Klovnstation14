using System.Numerics;
using Content.Client.Actions;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;

namespace Content.Client._KS14.Actions.UI;

/// <summary>
///     One action button taken off the bar and set loose on the screen. Click it to use the action as usual. While
///         the action layout is unlocked, drag the button itself to move it (or onto the bar, or onto the bin), and
///         right-click it to put it back on the bar.
/// </summary>
public sealed class KsFloatingActionWindow : BaseWindow
{
    public readonly EntityUid ActionUid;
    public readonly ActionButton Button;

    /// <summary>
    ///     Raised when the button is pressed with the left mouse button: the start of a click, or of a drag.
    /// </summary>
    public event Action<GUIBoundKeyEventArgs, KsFloatingActionWindow>? OnPress;

    /// <summary>
    ///     Raised when the left mouse button comes back up: the end of a click (to use the action), or of a drag.
    /// </summary>
    public event Action<GUIBoundKeyEventArgs, KsFloatingActionWindow>? OnRelease;

    /// <summary>
    ///     Raised when the button is right-clicked (to put it back on the bar).
    /// </summary>
    public event Action<KsFloatingActionWindow>? OnDock;

    /// <summary>
    ///     Raised when the button is shift-clicked (to bind a key to it).
    /// </summary>
    public event Action<KsFloatingActionWindow>? OnBind;

    /// <summary>
    ///     Raised when the button is ctrl-clicked (to toggle quick-cast).
    /// </summary>
    public event Action<KsFloatingActionWindow>? OnQuickCastToggle;

    public KsFloatingActionWindow(IEntityManager entityManager, ActionsSystem actionsSystem, EntityUid actionUid)
    {
        ActionUid = actionUid;
        MouseFilter = MouseFilterMode.Stop;

        Button = new ActionButton(entityManager);
        Button.UpdateData(actionUid, actionsSystem);
        Button.Label.Text = string.Empty;
        Button.ActionPressed += OnButtonPressed;
        Button.ActionUnpressed += OnButtonUnpressed;
        AddChild(Button);
    }

    private void OnButtonPressed(GUIBoundKeyEventArgs args, ActionButton button)
    {
        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            args.Handle();
            OnDock?.Invoke(this);
            return;
        }

        // Shift-click arrives as text-select or examine, depending on what's under the cursor.
        if (args.Function == EngineKeyFunctions.TextCursorSelect || args.Function == Content.Shared.Input.ContentKeyFunctions.ExamineEntity)
        {
            args.Handle();
            OnBind?.Invoke(this);
            return;
        }

        if (args.Function == Content.Shared.Input.ContentKeyFunctions.TryPullObject || args.Function == EngineKeyFunctions.EditorGridPlace)
        {
            args.Handle();
            OnQuickCastToggle?.Invoke(this);
            return;
        }

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        OnPress?.Invoke(args, this);
    }

    private void OnButtonUnpressed(GUIBoundKeyEventArgs args, ActionButton button)
    {
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        OnRelease?.Invoke(args, this);
    }

    /// <summary>
    ///     Show the key bound to this button, or nothing.
    /// </summary>
    public void SetKeyLabel(string text)
    {
        Button.Label.Text = text;
        Button.Label.Visible = text.Length > 0;
    }

    protected override DragMode GetDragModeFor(Vector2 relativeMousePos)
    {
        // Moving is a drag of the button itself, through the bar's own drag and drop, so the window never drags.
        return DragMode.None;
    }
}
