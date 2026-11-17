using Content.Server.Shuttles.Components;
using Content.Shared._KS14.Sensors;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

// KS14
public sealed partial class ShuttleConsoleSystem
{
    /// <summary>Called by KsSensorSystem on the sensor tick.</summary>
    public void KsRefreshSensorConsoles()
    {
        DockingInterfaceState? dockState = null;

        // Marker-gated: vanilla shuttle and cargo consoles keep updating on the normal
        // upstream cadence. Only fork consoles need a push when the contact picture changes.
        var query = AllEntityQuery<KsSensorConsoleComponent, ShuttleConsoleComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            if (!_ui.IsUiOpen(uid, ShuttleConsoleUiKey.Key))
                continue;

            UpdateState(uid, ref dockState);
        }
    }

    /// <summary>
    ///     Called when a console's UI opens (deliver the current picture immediately)
    ///         and when it closes (UpdateState strips contacts for closed UIs, scrubbing
    ///         the stored BUI state that PVS replicates to nearby clients).
    /// </summary>
    public void KsRefreshConsole(EntityUid consoleUid)
    {
        // Vanilla consoles carry no sensor picture to deliver or scrub.
        if (!HasComp<KsSensorConsoleComponent>(consoleUid))
            return;

        DockingInterfaceState? dockState = null;
        UpdateState(consoleUid, ref dockState);
    }
}
