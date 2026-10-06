namespace Content.Server._KS14.NPC.Doors;

/// <summary>
///     An NPC's dealings with doors: how it gets through ones it cannot open, and the doors it, or someone in its squad,
///         has found will not open for it however they looked - its access revoked, say - so it should not count on
///         them without a tool to force them. Each squad member keeps its own copy of those.
///     <para>
///         Added when first needed: an NPC without one behaves as one with the defaults. Remembered doors are dropped
///             once they expire, when read: nothing ticks them.
///     </para>
/// </summary>
[RegisterComponent]
[Access(typeof(NpcDoorSystem))]
public sealed partial class NpcDoorUserComponent : Component
{
    public const bool DefaultBreachWhenBlocked = true;

    /// <summary>
    ///     Whether, when the way it is going runs through a door that will not open for it, it forces the door with
    ///         something it carries - a prying tool, an access breaker - rather than stand at it. See
    ///         <see cref="NpcDoorSystem.TryBreachBlockingDoor"/>.
    /// </summary>
    [DataField]
    public bool BreachWhenBlocked = DefaultBreachWhenBlocked;

    /// <summary>
    ///     How long a door stays a no-go once found to be one.
    /// </summary>
    [DataField]
    public TimeSpan ForgetAfter = TimeSpan.FromSeconds(120);

    /// <summary>
    ///     Whether a door that fooled it becomes a no-go for its whole squad, not just for it. Off, each squadmate has
    ///         to be refused by the door itself.
    /// </summary>
    [DataField]
    public bool WarnsSquad = true;

    /// <summary>
    ///     How long a door it walked up to and could not get through by any means stays out of its paths. See
    ///         <see cref="BlockedDoors"/>.
    /// </summary>
    [DataField]
    public TimeSpan BlockedForgetAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long it keeps going round a door it could force (see <see cref="DetourDoors"/>), and how long a door found
    ///         to be the only way stays one it forces (see <see cref="ForceableDoors"/>).
    /// </summary>
    [DataField]
    public TimeSpan DetourForgetAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How much further, in tiles, the way round a door it could force may be than the way through it, and still be
    ///         taken. Past this, it forces the door. See <see cref="NpcDoorSystem.IsDetourWorthTaking"/>.
    /// </summary>
    [DataField]
    public float MaxDetourExtraDistance = 15f;

    /// <summary>
    ///     How far it had left to go, through the door, when it first turned to go round one. What a way round is
    ///         measured against.
    /// </summary>
    [ViewVariables]
    public float DetourBaseDistance;

    /// <summary>
    ///     Each no-go door, and when it stops being one.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> NoGoDoors = new();

    /// <summary>
    ///     Doors it walked up to and could not get through at all - not by hand, not with anything it carries - and
    ///         when it may try them again. Its own paths go round them meanwhile (see
    ///         <see cref="NpcDoorSystem.GetBlockedDoors"/>), rather than walking it back into the same door. Its own
    ///         alone, unlike <see cref="NoGoDoors"/>: a squadmate may well carry what it lacked.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> BlockedDoors = new();

    /// <summary>
    ///     Doors shut to it that it could force, which it is going round instead - forcing a door is loud, and spends
    ///         an access breaker's charges - and until when. Its paths avoid them, like <see cref="BlockedDoors"/>. Moved
    ///         to <see cref="ForceableDoors"/> if that leaves no way at all. See
    ///         <see cref="NpcDoorSystem.TryDetourAroundDoor"/>.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> DetourDoors = new();

    /// <summary>
    ///     Doors found to be the only way, which it forces rather than tries to go round again, and until when.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> ForceableDoors = new();

    /// <summary>
    ///     When this NPC was last refused by a door it believed it could open, if it has not yet said so. See
    ///         <c>DoorRefusedPrecondition</c>.
    /// </summary>
    [ViewVariables]
    public TimeSpan? RefusedAt;
}
