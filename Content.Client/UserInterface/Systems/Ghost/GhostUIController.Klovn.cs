using Content.Client._KS14.GhostRespawn;
using Robust.Client.UserInterface;

namespace Content.Client.UserInterface.Systems.Ghost;

public sealed partial class GhostUIController
{
    [UISystemDependency] private readonly GhostRespawnSystem? _ghostRespawnSystem = default;

    public void OnSystemLoaded(GhostRespawnSystem system)
    {
        system.RespawnTimeUpdated += OnRespawnTimeUpdated;
        system.EnabledUpdated += OnEnabledUpdated;
    }

    public void OnSystemUnloaded(GhostRespawnSystem system)
    {
        system.RespawnTimeUpdated -= OnRespawnTimeUpdated;
        system.EnabledUpdated -= OnEnabledUpdated;
    }

    private void OnRespawnTimeUpdated(TimeSpan? time)
    {
        if (Gui is not { } gui)
            return;

        if (time is not { })
            gui.AlertedForRespawn = false;

        gui.RespawnTime = time;
    }

    private void OnEnabledUpdated(bool enabled)
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
