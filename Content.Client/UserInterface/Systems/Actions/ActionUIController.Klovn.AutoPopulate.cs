using System.Collections.Generic;
using Content.Shared.Actions.Components;

namespace Content.Client.UserInterface.Systems.Actions;

/// <summary>
///     Honours <see cref="ActionComponent.AutoPopulate"/> while playing, not only when the bar is first filled: an action
///         that doesn't belong on the bar stays attached and usable (from a menu, say) but off it, and goes on or comes off
///         when the server changes its mind.
/// </summary>
public sealed partial class ActionUIController
{
    private readonly Dictionary<EntityUid, bool> _knownAutoPopulate = new();

    /// <returns>True if the action should stay off the bar.</returns>
    private bool KeepOffBar(Entity<ActionComponent> action)
    {
        _knownAutoPopulate[action.Owner] = action.Comp.AutoPopulate;
        return !action.Comp.AutoPopulate;
    }

    private void ForgetAutoPopulate(EntityUid actionUid)
    {
        _knownAutoPopulate.Remove(actionUid);
    }

    /// <summary>
    ///     Put actions whose AutoPopulate turned on onto the bar, and take off those whose turned off.
    /// </summary>
    private void SyncAutoPopulate()
    {
        if (_actionsSystem == null)
            return;

        foreach (var action in _actionsSystem.GetClientActions())
        {
            // First sighting (an action from the initial load): just remember it.
            if (!_knownAutoPopulate.TryGetValue(action.Owner, out var known))
            {
                _knownAutoPopulate[action.Owner] = action.Comp.AutoPopulate;
                continue;
            }

            if (known == action.Comp.AutoPopulate)
                continue;

            _knownAutoPopulate[action.Owner] = action.Comp.AutoPopulate;
            if (action.Comp.AutoPopulate)
                AddActionToRoot(action.Owner);
            else
                RemoveActionFromFolders(action.Owner);

            SaveActionConfigurationAfterChange();
        }
    }

    /// <summary>
    ///     Use an action as if its button were pressed, whether or not it's on the bar: an instant action goes off, a
    ///         targeted one starts aiming.
    /// </summary>
    public void KsUseAction(EntityUid actionUid)
    {
        if (_actionsSystem?.GetAction(actionUid) is not { } action)
            return;

        if (_actionsSystem.KsTryUseWithoutAiming(action))
            return;

        if (EntityManager.TryGetComponent<TargetActionComponent>(action, out var target))
            ToggleTargeting((action, action, target));
        else
            _actionsSystem.TriggerAction(action);
    }
}
