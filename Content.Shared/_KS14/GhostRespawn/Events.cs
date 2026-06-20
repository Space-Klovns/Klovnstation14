using Robust.Shared.Serialization;

namespace Content.Shared._KS14.GhostRespawn;

/// <summary>
///     Server -> client, to sync the clients own ghostrespawn timer
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostRespawnTimeMessage(TimeSpan? time) : EntityEventArgs
{
    public TimeSpan? Time = time;
}

/// <summary>
///     Client -> server, to request a respawn if possible
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostRespawnActMessage : EntityEventArgs;
