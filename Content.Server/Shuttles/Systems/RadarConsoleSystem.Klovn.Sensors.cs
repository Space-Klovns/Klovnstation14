using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

// KS14
public sealed partial class RadarConsoleSystem
{
    /// <summary>Called by KsSensorSystem on the sensor tick.</summary>
    public void KsRefreshOpenUis()
    {
        var query = AllEntityQuery<RadarConsoleComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!_uiSystem.IsUiOpen(uid, RadarConsoleUiKey.Key))
                continue;

            UpdateState(uid, component);
        }
    }

    /// <summary>
    ///     Called when a console's UI opens (deliver the current picture immediately)
    ///         and when it closes (UpdateState strips contacts for closed UIs, scrubbing
    ///         the stored BUI state that PVS replicates to nearby clients).
    /// </summary>
    public void KsRefreshConsole(EntityUid uid, RadarConsoleComponent component)
    {
        UpdateState(uid, component);
    }
}
