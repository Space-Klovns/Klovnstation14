using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     On anything that has fired a gun lately, NPC or not: when it last did, which anyone near could hear, and which way,
///         its line of fire. What NPCs go by to tell a hostile that has stopped shooting - reloading, or out - from one that
///         never started, and to keep out of anyone's fire. See <see cref="NpcGunfireSystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcGunfireSystem))]
public sealed partial class NpcRecentShooterComponent : Component
{
    [ViewVariables, AutoPausedField]
    public TimeSpan LastShotAt;

    /// <summary>
    ///     Where the last shot was fired from, relative to the grid or map it was fired on.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates LastShotFrom;

    /// <summary>
    ///     What the last shot was aimed at, relative to the grid or map.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates LastShotTo;

    /// <summary>
    ///     How far the last shot's line of fire goes before a wall stops it, worked out the first time anyone asks, or
    ///         negative until then. See <see cref="NpcGunfireSystem.GetLineOfFire"/>.
    /// </summary>
    [ViewVariables]
    public float LastShotReach = -1f;
}
