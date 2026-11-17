using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.RCD;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.RCD.Systems;

public sealed partial class RCDSystem
{
    [Dependency] private KsNoBuildZoneSystem _ksNoBuildZoneSystem = default!;

    /// <summary>
    ///     Whether the tile at <paramref name="position"/> lies in a no-build zone, which refuses every RCD operation.
    ///         The placement ghost runs this too, so it shows the tile as impossible before anyone tries.
    /// </summary>
    private bool KsIsInNoBuildZone(EntityUid uid, EntityUid gridUid, MapGridComponent mapGrid, Vector2i position, EntityUid user, bool popMsgs)
    {
        var tileCoordinates = _mapSystem.ToCenterCoordinates(gridUid, position, mapGrid);
        if (!_ksNoBuildZoneSystem.IsForbidden(_transform.ToMapCoordinates(tileCoordinates)))
            return false;

        if (popMsgs)
            _popup.PopupClient(KsPopupMessage.Create("ks-rcd-no-build-zone"), uid, user);

        return true;
    }
}
