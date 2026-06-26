using Content.Client._KS14.GhostRespawn;
using Robust.Client.UserInterface;

namespace Content.Client.UserInterface.Systems.Ghost;

public sealed partial class GhostUIController
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

    private void OnLocalRespawnTimeUpdated(TimeSpan? time)
    {
        if (Gui is not { } gui)
            return;

        gui.AlertedForRespawn = false;
        gui.RespawnTime = time;
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
