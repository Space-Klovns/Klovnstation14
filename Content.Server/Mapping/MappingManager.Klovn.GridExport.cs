// ============================================================================
// KS14 DISCLAIMER: This vanilla-namespace partial contains the heavily adapted
// Eclipsion PNG grid export PVS port (84b343cf, 6b7453d4).
// ============================================================================
using Content.Shared.Administration;
using Content.Shared._KS14.Mapping;
using Robust.Server.GameStates;
using Robust.Shared.Map.Components;

namespace Content.Server.Mapping;

public sealed partial class MappingManager
{
    private partial void InitializeGridScreenshotExport()
    {
        _net.RegisterNetMessage<MappingScreenshotPvsMessage>(OnGridScreenshotPvs);
    }

    private void OnGridScreenshotPvs(MappingScreenshotPvsMessage message)
    {
        if (!_players.TryGetSessionByChannel(message.MsgChannel, out var session) ||
            !_admin.IsAdmin(session, true) ||
            !(_admin.HasAdminFlag(session, AdminFlags.Host) || _admin.HasAdminFlag(session, AdminFlags.Mapping)) ||
            !_ent.TryGetEntity(message.Grid, out var grid) ||
            !_ent.HasComponent<MapGridComponent>(grid))
        {
            return;
        }

        var pvsOverrideSystem = _systems.GetEntitySystem<PvsOverrideSystem>();
        if (message.Enabled)
            pvsOverrideSystem.AddSessionOverride(grid.Value, session);
        else
            pvsOverrideSystem.RemoveSessionOverride(grid.Value, session);
    }
}