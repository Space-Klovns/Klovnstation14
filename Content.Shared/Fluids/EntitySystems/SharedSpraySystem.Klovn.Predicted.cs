using System.Numerics;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.PredictedSpawning;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.Fluids.EntitySystems;

/// <summary>
///     Spraying, moved here from the server's <c>SpraySystem</c> so that the sprayer's client predicts it. A spray its
///         player's own click or activation makes is a user-mode predicted spawn
///         (<see cref="KsSharedPredictedSpawnSystem"/>): that client keeps its own vapors, and the server's are hidden
///         from it. Anything else (a verb, an action, a test) sprays the server's vapors alone.
/// </summary>
public abstract partial class SharedSpraySystem
{
    [Dependency] private SharedGravitySystem _gravitySystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private UseDelaySystem _useDelaySystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedVaporSystem _vaporSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private KsSharedPredictedSpawnSystem _predictedSpawnSystem = default!;

    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    /// <summary>
    ///     Pushes the grid the sprayer stands on away from the spray. The server's alone: grids are not predicted.
    /// </summary>
    protected virtual void KsPushGrid(EntityUid gridUid, Vector2 impulse, Vector2 localPosition)
    {
    }

    [SubscribeLocalEvent]
    private void OnActivateInWorld(Entity<SprayComponent> entity, ref UserActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var targetMapPos = _transformSystem.GetMapCoordinates(Transform(args.Target));

        KsSpray(entity, targetMapPos, args.User, predictingUser: args.User);
    }

    [SubscribeLocalEvent]
    private void OnAfterInteract(Entity<SprayComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var clickPos = _transformSystem.ToMapCoordinates(args.ClickLocation);

        KsSpray(entity, clickPos, args.User, predictingUser: args.User);
    }

    private void KsSpray(Entity<SprayComponent> entity, EntityUid? user)
    {
        var xform = Transform(entity);
        var throwing = xform.LocalRotation.ToWorldVec() * entity.Comp.SprayDistance;
        var direction = xform.Coordinates.Offset(throwing);

        KsSpray(entity, _transformSystem.ToMapCoordinates(direction), user, predictingUser: null);
    }

    /// <param name="predictingUser">
    ///     The entity whose client predicts this spray, when it is that player's own click or activation, so that their
    ///         vapors are their client's kept copies. Null for anything their client does not run as well.
    /// </param>
    private void KsSpray(Entity<SprayComponent> entity, MapCoordinates mapcoord, EntityUid? user, EntityUid? predictingUser)
    {
        if (!_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.Solution, out var soln, out var solution))
            return;

        var ev = new SprayAttemptEvent(user);
        RaiseLocalEvent(entity, ref ev);
        if (ev.Cancelled)
        {
            if (ev.CancelPopupMessage != null && user != null)
                _popupSystem.PopupEntity(KsPopupMessage.Create(ev.CancelPopupMessage), entity.Owner, user.Value);
            return;
        }

        if (_useDelaySystem.IsDelayed((entity, null)))
            return;

        if (solution.Volume <= 0)
        {
            if (user != null)
                _popupSystem.PopupEntity(KsPopupMessage.Create(entity.Comp.SprayEmptyPopupMessage, ("entity", entity)), entity.Owner, user.Value);
            return;
        }

        var sprayerXform = Transform(entity);

        var sprayerMapPos = _transformSystem.GetMapCoordinates(sprayerXform);
        var clickMapPos = mapcoord;

        var diffPos = clickMapPos.Position - sprayerMapPos.Position;
        if (diffPos == Vector2.Zero || diffPos == Vector2Helpers.NaN)
            return;

        var diffNorm = diffPos.Normalized();
        var diffLength = diffPos.Length();

        if (diffLength > entity.Comp.SprayDistance)
        {
            diffLength = entity.Comp.SprayDistance;
        }

        var diffAngle = diffNorm.ToAngle();

        // Vectors to determine the spawn offset of the vapor clouds.
        var threeQuarters = diffNorm * 0.75f;
        var quarter = diffNorm * 0.25f;

        var amount = Math.Max(Math.Min((solution.Volume / entity.Comp.TransferAmount).Int(), entity.Comp.VaporAmount), 1);
        var spread = entity.Comp.VaporSpread / amount;

        for (var i = 0; i < amount; i++)
        {
            var rotation = new Angle(diffAngle + Angle.FromDegrees(spread * i) -
                                     Angle.FromDegrees(spread * (amount - 1) / 2));

            // Calculate the destination for the vapor cloud. Limit to the maximum spray distance.
            var target = sprayerMapPos
                .Offset((diffNorm + rotation.ToVec()).Normalized() * diffLength + quarter);

            var distance = (target.Position - sprayerMapPos.Position).Length();
            if (distance > entity.Comp.SprayDistance)
                target = sprayerMapPos.Offset(diffNorm * entity.Comp.SprayDistance);

            var adjustedSolutionAmount = entity.Comp.TransferAmount / entity.Comp.VaporAmount;

            // Spawn the vapor cloud onto the grid/map the user is present on. Offset the start position based on how far the target destination is.
            var vaporPos = sprayerMapPos.Offset(distance < 1 ? quarter : threeQuarters);
            var vapor = _predictedSpawnSystem.PredictedSpawn(entity.Comp.SprayedPrototype, vaporPos, user: predictingUser);
            var vaporXform = Transform(vapor);

            _transformSystem.SetWorldRotation(vaporXform, rotation);

            _vaporSystem.TryAddSolution(vapor, soln.Value, adjustedSolutionAmount);

            // impulse direction is defined in world-coordinates, not local coordinates
            var impulseDirection = rotation.ToVec();
            var time = diffLength / entity.Comp.SprayVelocity;

            _vaporSystem.Start(vapor, vaporXform, impulseDirection * diffLength, entity.Comp.SprayVelocity, target, time, user,
                predicted: predictingUser != null);

            var thingGettingPushed = entity.Owner;
            if (_containerSystem.TryGetOuterContainer(entity, sprayerXform, out var container))
                thingGettingPushed = container.Owner;

            if (_physicsQuery.TryComp(thingGettingPushed, out var body))
            {
                if (_gravitySystem.IsWeightless(thingGettingPushed))
                {
                    // push back the player
                    _physicsSystem.ApplyLinearImpulse(thingGettingPushed, -impulseDirection * entity.Comp.PushbackAmount, body: body);
                }
                else
                {
                    // push back the grid the player is standing on
                    var userTransform = Transform(thingGettingPushed);
                    if (userTransform.GridUid is { } gridUid && gridUid == userTransform.ParentUid)
                    {
                        // apply both linear and angular momentum depending on the player position
                        KsPushGrid(gridUid, -impulseDirection * entity.Comp.PushbackAmount, userTransform.LocalPosition);
                    }
                }
            }
        }

        _audioSystem.PlayPredicted(entity.Comp.SpraySound, entity, predictingUser, entity.Comp.SpraySound.Params.WithVariation(0.125f));

        _useDelaySystem.TryResetDelay(entity);
    }
}
