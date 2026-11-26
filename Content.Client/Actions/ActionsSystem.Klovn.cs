using Content.Shared._KS14.Actions;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.Map;

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

    /// <summary>
    ///     What came of trying to quick-cast an art at the cursor.
    /// </summary>
    public enum KsQuickCastResult
    {
        /// <summary>It went off (or at least was sent to the server, which has the last word).</summary>
        Cast,
        /// <summary>The cursor is out of the art's reach; nothing was spent.</summary>
        TooFar,
        /// <summary>There was nothing to cast it on under the cursor, so it should be aimed the usual way.</summary>
        NeedsAim,
    }

    /// <summary>
    ///     Use a targeted action straight away at the cursor, without the second click: a spot-targeted one on the spot
    ///         under the mouse, a person-targeted one on whoever is under it. Range is checked like any cast.
    /// </summary>
    public KsQuickCastResult KsQuickCast(Entity<ActionComponent> action, MapCoordinates mouse, EntityUid? underMouse)
    {
        if (_playerManager.LocalEntity is not { } user)
            return KsQuickCastResult.NeedsAim;

        var transform = EntityManager.System<SharedTransformSystem>();

        if (TryComp<EntityTargetActionComponent>(action, out var entityTarget))
        {
            if (underMouse is not { } target)
                return KsQuickCastResult.NeedsAim;

            if (!ValidateEntityTarget(user, target, (action, entityTarget)))
            {
                // Someone there, but out of reach (or not something it works on).
                if (TryComp<TargetActionComponent>(action, out var reach) && reach.Range > 0
                    && !transform.InRange(user, target, reach.Range))
                    return KsQuickCastResult.TooFar;

                return KsQuickCastResult.NeedsAim;
            }

            RaisePredictiveEvent(new RequestPerformActionEvent(GetNetEntity(action), GetNetEntity(target)));
            return KsQuickCastResult.Cast;
        }

        if (TryComp<WorldTargetActionComponent>(action, out var worldTarget))
        {
            if (mouse.MapId == MapId.Nullspace)
                return KsQuickCastResult.NeedsAim;

            var coordinates = transform.ToCoordinates(mouse);
            if (!ValidateWorldTarget(user, coordinates, (action, worldTarget)))
                return KsQuickCastResult.TooFar;

            RaisePredictiveEvent(new RequestPerformActionEvent(GetNetEntity(action), GetNetCoordinates(coordinates)));
            return KsQuickCastResult.Cast;
        }

        return KsQuickCastResult.NeedsAim;
    }
}
