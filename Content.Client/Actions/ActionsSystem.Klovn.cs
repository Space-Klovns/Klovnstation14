using Content.Shared._KS14.Actions;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;

namespace Content.Client.Actions;

public sealed partial class ActionsSystem
{
    /// <summary>
    ///     If this targeted action doesn't need aiming right now, use it at once on the player's own spot.
    /// </summary>
    /// <returns>True if it was used.</returns>
    public bool KsTryUseWithoutAiming(Entity<ActionComponent> action)
    {
        if (!TryComp<KsSkipTargetingComponent>(action, out var skipComponent) || !skipComponent.Skip
            || _playerManager.LocalEntity is not { } user)
            return false;

        var coordinates = Transform(user).Coordinates;
        if (HasComp<EntityTargetActionComponent>(action))
            RaisePredictiveEvent(new RequestPerformActionEvent(GetNetEntity(action), GetNetEntity(user)));
        else
            RaisePredictiveEvent(new RequestPerformActionEvent(GetNetEntity(action), GetNetCoordinates(coordinates)));

        return true;
    }
}
