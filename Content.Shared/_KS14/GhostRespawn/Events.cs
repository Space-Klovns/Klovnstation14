using Robust.Shared.Serialization;

namespace Content.Shared._KS14.GhostRespawn;

/// <summary>
///     Server -> client, to sync the clients own ghostrespawn timer
/// </summary>
/// <param name="time">When the respawn timer is up, or null when no respawn is pending.</param>
/// <param name="gate">What holds the respawn back past the timer, if anything does.</param>
[Serializable, NetSerializable]
public sealed class GhostRespawnTimeMessage(TimeSpan? time, GhostRespawnGate? gate = null) : EntityEventArgs
{
    public TimeSpan? Time = time;

    public GhostRespawnGate? Gate = gate;
}

/// <summary>
///     Something other than the respawn timer refusing a ghost's respawn for now, such as a gamerule that allows it
///         only at certain times. Resent to a pending ghost whenever it changes.
/// </summary>
/// <param name="Reason">Why, for the ghost to read.</param>
/// <param name="OpensAt">When the respawn should be allowed, if that is known.</param>
[Serializable, NetSerializable]
public readonly record struct GhostRespawnGate(LocId Reason, TimeSpan? OpensAt);

/// <summary>
///     Server -> client: whether respawning is on, while something (a gamerule) forces it to differ from the replicated
///         <c>klovn.ghostrespawn.enabled</c> cvar. Null hands it back to the cvar.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostRespawnEnabledMessage(bool? enabled) : EntityEventArgs
{
    public bool? Enabled = enabled;
}

/// <summary>
///     Client -> server, to request a respawn if possible
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostRespawnActMessage : EntityEventArgs;
