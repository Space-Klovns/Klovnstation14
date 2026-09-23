using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ZLevel.Elevators;

/// <summary>
///     Present on an elevator grid while it is crossing a gap between two z-levels, or waiting out its dwell
///         time at one.
/// </summary>
/// <remarks>
///     Progress is derived from the clock rather than integrated: <see cref="Height"/> is recomputed every
///         tick from <see cref="StartTime"/> and <see cref="EndTime"/>, both of which are networked and
///         paused with the entity. Client and server therefore agree exactly, with nothing to re-predict and
///         no drift to correct - unlike a velocity, which both sides would have to integrate identically
///         forever to stay in step.
/// </remarks>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedZLevelElevatorSystem))]
public sealed partial class ActiveZLevelElevatorComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime = TimeSpan.Zero;

    /// <summary>
    ///     When the current leg or dwell began, so that progress through it can be read off the clock.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan StartTime = TimeSpan.Zero;

    /// <summary>
    ///     Whether the elevator is crossing a gap or sitting at a floor.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ZLevelElevatorState State = ZLevelElevatorState.Travelling;

    /// <summary>
    ///     How far through the gap it is crossing the elevator is, from 0 at the lower z-level's floor plane
    ///         to 1 at the upper one's.
    /// </summary>
    /// <remarks>
    ///     The same 0..1 convention <see cref="Physics.KsZLevelTransitComponent.Height"/> uses for falling
    ///         entities, so anything reading altitude reads both the same way. It is not that component
    ///         because that one also knocks its owner down, blocks its movement and vetoes every contact it
    ///         has - which on a grid would drop every passenger through the elevator's own floor.
    ///     Deliberately neither a DataField nor networked: it is recomputed from the clock every tick on
    ///         both sides, so replicating it would spend bandwidth every tick to say something the client
    ///         can already work out exactly. It is pushed onto the gap map the elevator is riding, which is
    ///         what everything else reads.
    /// </remarks>
    [ViewVariables]
    public float Height;

    /// <summary>
    ///     Whether this leg is going up. Only meaningful while <see cref="ZLevelElevatorState.Travelling"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Rising;

    /// <summary>
    ///     The z-level this leg set out from.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid DepartedZLevel;

    /// <summary>
    ///     The z-level this leg is heading for.
    /// </summary>
    /// <remarks>
    ///     Recorded at departure rather than walked for again on arrival, so that a stack relinked mid-flight
    ///         cannot land the elevator somewhere nobody asked it to go. If the recorded target has stopped
    ///         being a z-level by the time it gets there, it stops instead of guessing.
    /// </remarks>
    [DataField, AutoNetworkedField]
    public EntityUid TargetZLevel;
}
