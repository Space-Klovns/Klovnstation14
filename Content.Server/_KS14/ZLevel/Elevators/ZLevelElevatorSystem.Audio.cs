using Content.Shared._KS14.ZLevel.Elevators;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.ZLevel.Elevators;

/// <summary>
///     What an elevator sounds like: a clunk as it sets off, a hum while it runs, and a clunk as it stops.
/// </summary>
/// <remarks>
///     Driven from the departure and stop events rather than from the active component's lifecycle, because
///         that component is taken off and put back on at every floor the elevator passes through - a
///         three-floor journey is three legs, and a lift does not start up twice on the way.
/// </remarks>
public sealed partial class ZLevelElevatorSystem
{
    /// <summary>
    ///     Sets the hum going, and clunks once if this is the start of a journey rather than its next leg.
    /// </summary>
    private void StartMovementAudio(Entity<ZLevelElevatorComponent> entity)
    {
        // Already running, so this is the next leg of a journey already under way rather than a new one.
        if (!TerminatingOrDeleted(entity.Comp.MovementAudioUid))
            return;

        _audioSystem.PlayPvs(entity.Comp.StartSound, entity.Owner);

        if (entity.Comp.MovementSound is not { } movementSound ||
            !_mapGridQuery.TryGetComponent(entity.Owner, out var gridComponent))
            return;

        // The middle of the platform rather than the grid's origin, which for a grid is an arbitrary corner
        //      of its chunk grid and can sit well outside the tiles themselves.
        var audioEntity = _audioSystem.PlayPvs(
            movementSound,
            new EntityCoordinates(entity.Owner, gridComponent.LocalAABB.Center),
            movementSound.Params.WithLoop(true)
        );

        entity.Comp.MovementAudioUid = audioEntity?.Entity;
    }

    /// <summary>
    ///     Cuts the hum and clunks as the elevator settles.
    /// </summary>
    private void StopMovementAudio(Entity<ZLevelElevatorComponent> entity)
    {
        entity.Comp.MovementAudioUid = _audioSystem.Stop(entity.Comp.MovementAudioUid);

        _audioSystem.PlayPvs(entity.Comp.StopSound, entity.Owner);
    }

    /// <summary>
    ///     An elevator that stops existing mid-journey must not leave its hum behind.
    /// </summary>
    /// <remarks>
    ///     The audio entity is parented to the grid, so deleting the grid ordinarily takes it too - but an
    ///         elevator can also simply lose the component, which leaves the grid and the sound on it.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnElevatorShutdown(Entity<ZLevelElevatorComponent> entity, ref ComponentShutdown args)
    {
        entity.Comp.MovementAudioUid = _audioSystem.Stop(entity.Comp.MovementAudioUid);
    }
}
