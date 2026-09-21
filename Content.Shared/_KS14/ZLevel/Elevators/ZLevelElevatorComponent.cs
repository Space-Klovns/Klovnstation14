using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ZLevel.Elevators;

/// <summary>
///     Added to grids that act like elevators - they can move up/down Z-levels on
///         command.
/// </summary>
/// <remarks>
///     An elevator's floors are not authored anywhere: every z-level in the grid's stack is one, numbered by
///         position. That is deliberate, because a map is saved standalone and only joins a stack at runtime,
///         so there is nothing an elevator could usefully have been told about its floors when it was drawn.
/// </remarks>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class ZLevelElevatorComponent : Component
{
    // These are just kinda optional and are for if someone wants to do something funny with yaml

    /// <summary>
    ///     Trigger keys in to go up.
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<string> UpKeysIn = [];

    /// <summary>
    ///     Trigger keys in to go down.
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<string> DownKeysIn = [];

    /// <summary>
    ///     Which shaft this elevator belongs to, matched against the same field on controllers, call buttons
    ///         and signal emitters.
    /// </summary>
    /// <remarks>
    ///     Only needed where one z-level stack holds more than one elevator. Left null, everything resolves
    ///         to whichever elevator in the stack is horizontally nearest, which is what a single shaft wants
    ///         and costs a mapper nothing.
    /// </remarks>
    [DataField, AutoNetworkedField]
    [Access(typeof(SharedZLevelElevatorSystem))]
    public string? ShaftId;

    /// <summary>
    ///     How long, in seconds, the elevator takes to cross one unit of a z-level's
    ///     <see cref="KsZLevelComponent.Depth"/>.
    /// </summary>
    /// <remarks>
    ///     Travel time is the crossed z-level's Depth times this, so a deep level takes proportionally longer
    ///         - the same relationship a falling entity has with Depth, just at a constant speed instead of
    ///         under gravity.
    /// </remarks>
    /// <remarks>
    ///     Not networked: the client works a leg out from the start and end times on
    ///     <see cref="ActiveZLevelElevatorComponent"/>, not from the speed that produced them.
    /// </remarks>
    [DataField]
    public float SecondsPerDepth = 4f;

    /// <summary>
    ///     How long the elevator waits at a floor it has stopped at before deciding where to go next.
    /// </summary>
    /// <remarks>
    ///     Not networked, for the same reason as <see cref="SecondsPerDepth"/>.
    /// </remarks>
    [DataField]
    public TimeSpan DwellTime = TimeSpan.FromSeconds(4);

    /// <summary>
    ///     Played once as the elevator sets off from a standstill.
    /// </summary>
    /// <remarks>
    ///     Once per journey rather than once per leg: a lift climbing three floors sets off once and passes
    ///         two of them without stopping.
    ///     These three have defaults, unlike most sounds, because an elevator is made out of an existing grid
    ///         by the zlevel_elevator command rather than spawned from a prototype - so there is no yaml for
    ///         anyone to set them in, and a null default would mean every elevator ever made is silent. They
    ///         are stand-ins from the existing machine sounds; replace them once a shaft has its own.
    /// </remarks>
    [DataField]
    public SoundSpecifier? StartSound = new SoundPathSpecifier("/Audio/Machines/blastdoor.ogg");

    /// <summary>
    ///     Played once as the elevator comes to rest, whether at a floor it was called to or because it ran
    ///         out of places to go.
    /// </summary>
    [DataField]
    public SoundSpecifier? StopSound = new SoundPathSpecifier("/Audio/Machines/blastdoor.ogg");

    /// <summary>
    ///     Looped for as long as the elevator is moving, from the middle of its grid.
    /// </summary>
    /// <remarks>
    ///     Parented to the grid rather than played at a position, so it rides along - including across the
    ///         map change onto a gap and off it again, which happens twice a leg.
    /// </remarks>
    [DataField]
    public SoundSpecifier? MovementSound = new SoundPathSpecifier("/Audio/Ambience/Objects/engine_hum.ogg");

    /// <summary>
    ///     The looping <see cref="MovementSound"/> currently playing, if any.
    /// </summary>
    /// <remarks>
    ///     Not a DataField and not networked: an audio stream is this round's, and the server is what decides
    ///         who hears it.
    /// </remarks>
    [ViewVariables]
    [Access(typeof(SharedZLevelElevatorSystem))]
    public EntityUid? MovementAudioUid;

    /// <summary>
    ///     The z-levels the elevator has been called to and has not served yet.
    /// </summary>
    /// <seealso cref="SharedZLevelElevatorSystem.TryCallToZLevel"/>
    [DataField, AutoNetworkedField]
    [Access(typeof(SharedZLevelElevatorSystem))]
    public HashSet<EntityUid> CalledZLevels = [];

    /// <summary>
    ///     Which way the elevator is currently sweeping.
    /// </summary>
    /// <remarks>
    ///     Kept across a dwell rather than cleared on arrival: an elevator that is on its way up should serve
    ///         a call above it before one behind it, however much nearer the one behind is. That is the whole
    ///         of what makes a lift feel like a lift rather than a taxi.
    /// </remarks>
    [DataField, AutoNetworkedField]
    [Access(typeof(SharedZLevelElevatorSystem))]
    public ZLevelElevatorDirection Direction = ZLevelElevatorDirection.Idle;
}
