using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.GhostRespawn;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.GhostRespawn;

public sealed class GhostRespawnSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;

    /// <summary>
    ///     Respawn time for the local client.
    ///         Null if there is none or you are not on the client.
    /// </summary>
    public TimeSpan? LocalRespawnTime { get; private set; }

    public bool Enabled { get; private set; }

    /// <summary>
    ///     Invoked with the new time that respawn will be allowed at.
    ///         If null, respawn is not allowed.
    /// </summary>
    public event Action<TimeSpan?>? RespawnTimeUpdated;

    /// <summary>
    ///     Invoked with the new time that respawn will be allowed at.
    ///         If null, respawn is not allowed.
    /// </summary>
    public event Action<bool>? EnabledUpdated;

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(KsCCVars.GhostRespawnEnabled, OnEnabledChanged, invokeImmediately: true);
        SubscribeNetworkEvent<GhostRespawnTimeMessage>(OnTimeMessage);
    }

    private void OnEnabledChanged(bool enabled)
    {
        Enabled = enabled;
        EnabledUpdated?.Invoke(enabled);
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
