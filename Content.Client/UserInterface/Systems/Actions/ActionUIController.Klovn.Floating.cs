using System.Collections.Generic;
using System.Linq;
using Content.Client._KS14.Actions.UI;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Systems.Actions;

/// <summary>
///     Alt-click a slot on the action bar to pop its action off the bar as a floating button you can drag anywhere on
///         screen. Right-click the floating button to put it back; shift-click it to give its art a key (see the rebind
///         partial: the key belongs to the art, so it works the same on the bar or off it). Floating buttons last
///         until you dock them or reconnect.
/// </summary>
public sealed partial class ActionUIController
{
    private readonly List<KsFloatingActionWindow> _floatingActions = new();

    /// <returns>True if this click popped the slot's action out.</returns>
    private bool TryFloatAction(ActionButton button)
    {
        if (_actionsSystem == null || button.Action is not { } action
            || IsFolderAction(button))
            return false;

        if (_floatingActions.Any(window => window.ActionUid == action.Owner))
            return true;

        var window = new KsFloatingActionWindow(EntityManager, _actionsSystem, action.Owner);
        window.OnUse += KsUseAction;
        window.OnDock += DockFloatingAction;
        window.OnBind += StartFloatingRebind;
        UIManager.WindowRoot.AddChild(window);
        LayoutContainer.SetPosition(window, _input.MouseScreenPosition.Position / UIManager.WindowRoot.UIScale);
        _floatingActions.Add(window);

        RemoveActionFromFolders(action.Owner);
        SaveActionConfigurationAfterChange();
        RefreshActionBar();
        return true;
    }

    private void DockFloatingAction(KsFloatingActionWindow window)
    {
        if (_rebindingAction == window.ActionUid)
            CancelSlotRebind();

        _floatingActions.Remove(window);
        window.Close();
        if (_actionsSystem?.GetAction(window.ActionUid) is { } action && action.Comp.AttachedEntity != null)
        {
            AddActionToRoot(window.ActionUid);
            SaveActionConfigurationAfterChange();
            RefreshActionBar();
        }
    }

    /// <summary>
    ///     A floating button whose action is gone goes with it.
    /// </summary>
    private void RemoveFloatingAction(EntityUid actionUid)
    {
        foreach (var window in _floatingActions.Where(window => window.ActionUid == actionUid).ToList())
        {
            _floatingActions.Remove(window);
            window.Close();
        }
    }

    private void CloseFloatingActions()
    {
        foreach (var window in _floatingActions)
            window.Close();

        _floatingActions.Clear();
    }
}
