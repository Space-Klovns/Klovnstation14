using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.GhostRespawn;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.GhostRespawn;

public sealed partial class GhostRespawnSystem : SharedGhostRespawnSystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;

    /// <summary>
    ///     Respawn time for the local client.
    ///         Null if there is none or you are not on the client.
    /// </summary>
    public TimeSpan? LocalRespawnTime { get; private set; }

    /// <summary>
    ///     What holds the local client's respawn back past <see cref="LocalRespawnTime"/>, if anything.
    /// </summary>
    public GhostRespawnGate? LocalGate { get; private set; }

    public bool LocalEnabled { get; private set; }

    private bool _cvarEnabled;

    /// <summary>
    ///     The server's word on <see cref="LocalEnabled"/>, while it differs from the cvar (a gamerule forcing it).
    /// </summary>
    private bool? _enabledOverride;

    /// <summary>
    ///     Invoked with the new time that respawn will be allowed at, and what else holds it back.
    ///         If the time is null, respawn is not allowed.
    /// </summary>
    public event Action<TimeSpan?, GhostRespawnGate?>? LocalRespawnTimeUpdated;

    /// <summary>
    ///     Invoked with whether respawning is enabled, when that changes.
    /// </summary>
    public event Action<bool>? LocalEnabledUpdated;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.GhostRespawnEnabled, OnEnabledChanged, invokeImmediately: true);
    }

    private void OnEnabledChanged(bool enabled)
    {
        _cvarEnabled = enabled;
        UpdateEnabled();
    }

    [SubscribeNetworkEvent]
    private void OnEnabledMessage(GhostRespawnEnabledMessage message)
    {
        _enabledOverride = message.Enabled;
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        LocalEnabled = _enabledOverride ?? _cvarEnabled;
        LocalEnabledUpdated?.Invoke(LocalEnabled);
    }

    [SubscribeNetworkEvent]
    private void OnTimeMessage(GhostRespawnTimeMessage message)
    {
        LocalRespawnTime = message.Time;
        LocalGate = message.Gate;
        LocalRespawnTimeUpdated?.Invoke(message.Time, message.Gate);
    }

    public void RequestRespawn()
    {
        var msg = new GhostRespawnActMessage();
        RaiseNetworkEvent(msg);
    }
}
