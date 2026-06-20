using Content.Client._KS14.GhostRespawn;
using Robust.Client.UserInterface;

namespace Content.Client.UserInterface.Systems.Ghost;

public sealed partial class GhostUIController
{
    [UISystemDependency] private readonly GhostRespawnSystem? _ghostRespawnSystem = default;

    public void OnSystemLoaded(GhostRespawnSystem system)
    {
        system.RespawnTimeUpdated += OnRespawnTimeUpdated;
    }

    public void OnSystemUnloaded(GhostRespawnSystem system)
    {
        system.RespawnTimeUpdated -= OnRespawnTimeUpdated;
    }

    private void OnRespawnTimeUpdated(TimeSpan? time)
    {
        if (Gui is not { } gui)
            return;

        if (time is not { })
            gui.AlertedForRespawn = false;

        gui.RespawnTime = time;
    }

    private void OnGhostRespawnPressed()
    {
        if (_ghostRespawnSystem is not { })
            return;

        _ghostRespawnSystem.RequestRespawn();
    }
}
