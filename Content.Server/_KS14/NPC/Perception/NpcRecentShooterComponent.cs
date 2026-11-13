using Content.Shared.Physics;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     On anything that has fired a gun lately, NPC or not: when it last did, which anyone near could hear, and its last
///         few shots' lines of fire. What NPCs go by to tell a hostile that has stopped shooting - reloading, or out - from
///         one that never started, and to keep out of anyone's fire. See <see cref="NpcGunfireSystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcGunfireSystem))]
public sealed partial class NpcRecentShooterComponent : Component
{
    /// <summary>
    ///     How many shots are kept: enough for a burst swept across a corridor to be a fan, not just its last round.
    /// </summary>
    public const int ShotHistory = 4;

    [ViewVariables, AutoPausedField]
    public TimeSpan LastShotAt;

    /// <summary>
    ///     The last <see cref="ShotHistory"/> shots, oldest overwritten first; <see cref="NextShot"/> is the next to go.
    /// </summary>
    [ViewVariables]
    public NpcRecentShot[] Shots = new NpcRecentShot[ShotHistory];

    [ViewVariables]
    public int NextShot;
}

/// <summary>
///     One shot's line of fire: from where, aimed where, and what its round collides with. <see cref="Reach"/>, how far it
///         flies before something anchored stops it, is worked out the first time anyone asks, and negative until then.
/// </summary>
public struct NpcRecentShot
{
    [ViewVariables] public TimeSpan At;
    [ViewVariables] public EntityCoordinates From;
    [ViewVariables] public EntityCoordinates To;
    [ViewVariables] public CollisionGroup Mask;
    [ViewVariables] public float Reach;
}
