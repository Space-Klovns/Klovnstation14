using Content.Client._KS14.GhostRespawn;
using Content.Shared._KS14.GhostRespawn;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client.UserInterface.Systems.Ghost;

public sealed partial class GhostUIController : IOnSystemChanged<GhostRespawnSystem>
{
    [UISystemDependency] private readonly GhostRespawnSystem? _ghostRespawnSystem = default;

    public void OnSystemLoaded(GhostRespawnSystem system)
    {
        system.LocalRespawnTimeUpdated += OnLocalRespawnTimeUpdated;
        system.LocalEnabledUpdated += OnLocalEnabledUpdated;
    }

    public void OnSystemUnloaded(GhostRespawnSystem system)
    {
        system.LocalRespawnTimeUpdated -= OnLocalRespawnTimeUpdated;
        system.LocalEnabledUpdated -= OnLocalEnabledUpdated;
    }

    /// <summary>
    ///     Brings the button up to date with what the system last heard, gate included.
    /// </summary>
    private void OnLocalRespawnTimeUpdated(TimeSpan? time)
        => OnLocalRespawnTimeUpdated(time, _ghostRespawnSystem?.LocalGate);

    private void OnLocalRespawnTimeUpdated(TimeSpan? time, GhostRespawnGate? gate)
    {
        if (Gui is not { } gui)
            return;

        gui.SetRespawnTime(time, gate);
    }

    private void OnLocalEnabledUpdated(bool enabled)
    {
        if (Gui is not { } gui)
            return;

        gui.SetRespawnsEnabled(enabled);
    }

    private void OnGhostRespawnPressed()
    {
        if (_ghostRespawnSystem is not { })
            return;

        _ghostRespawnSystem.RequestRespawn();
    }
}
