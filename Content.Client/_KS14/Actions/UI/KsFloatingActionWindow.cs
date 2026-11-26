using System.Numerics;
using Content.Client.Actions;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;

namespace Content.Client._KS14.Actions.UI;

/// <summary>
///     One action button taken off the bar and set loose on the screen. Drag it by the strip along its top; click the
///         button to use the action as usual; right-click it to put it back on the bar.
/// </summary>
public sealed class KsFloatingActionWindow : BaseWindow
{
    private const float GripHeight = 8f;

    public readonly EntityUid ActionUid;
    public readonly ActionButton Button;

    /// <summary>
    ///     Raised when the button is clicked (to use the action).
    /// </summary>
    public event Action<EntityUid>? OnUse;

    /// <summary>
    ///     Raised when the button is right-clicked (to put it back on the bar).
    /// </summary>
    public event Action<KsFloatingActionWindow>? OnDock;

    /// <summary>
    ///     Raised when the button is shift-clicked (to bind a key to it).
    /// </summary>
    public event Action<KsFloatingActionWindow>? OnBind;

    public KsFloatingActionWindow(IEntityManager entityManager, ActionsSystem actionsSystem, EntityUid actionUid)
    {
        ActionUid = actionUid;
        MouseFilter = MouseFilterMode.Stop;

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        root.AddChild(new PanelContainer
        {
            MinSize = new Vector2(0, GripHeight),
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#e8c060").WithAlpha(0.55f) },
            MouseFilter = MouseFilterMode.Pass,
            ToolTip = Loc.GetString("ks-floating-action-grip"),
        });

        Button = new ActionButton(entityManager);
        Button.UpdateData(actionUid, actionsSystem);
        Button.Label.Text = string.Empty;
        Button.ActionPressed += OnButtonPressed;
        root.AddChild(Button);
        AddChild(root);
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

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        args.Handle();
        OnUse?.Invoke(ActionUid);
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
        return relativeMousePos.Y <= GripHeight ? DragMode.Move : DragMode.None;
    }
}
