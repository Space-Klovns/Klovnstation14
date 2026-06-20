using Content.Shared._KS14.GhostRespawn;

namespace Content.Client._KS14.GhostRespawn;

public sealed class GhostRespawnSystem : EntitySystem
{
    /// <summary>
    ///     Respawn time for the local client.
    ///         Null if there is none or you are not on the client.
    /// </summary>
    public TimeSpan? LocalRespawnTime { get; private set; }

    /// <summary>
    ///     Invoked with the new time that respawn will be allowed at.
    ///         If null, respawn is not allowed.
    /// </summary>
    public event Action<TimeSpan?>? RespawnTimeUpdated;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<GhostRespawnTimeMessage>(OnTimeMessage);
    }

    private void OnTimeMessage(GhostRespawnTimeMessage message)
    {
        LocalRespawnTime = message.Time;
        RespawnTimeUpdated?.Invoke(message.Time);
    }

    public void RequestRespawn()
    {
        var msg = new GhostRespawnActMessage();
        RaiseNetworkEvent(msg);
    }
}
