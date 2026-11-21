using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.RCD;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.RCD.Systems;

public sealed partial class RCDSystem
{
    [Dependency] private KsNoBuildZoneSystem _ksNoBuildZoneSystem = default!;

    /// <summary>
    ///     Whether the tile at <paramref name="position"/> is on a map where RCDs are blocked, or lies in a no-build
    ///         zone: either refuses every RCD operation. Runs on the client too, predicted, and for the placement ghost,
    ///         which so shows the tile as impossible before anyone tries.
    /// </summary>
    private bool KsIsInNoBuildZone(EntityUid uid, EntityUid gridUid, MapGridComponent mapGrid, Vector2i position, EntityUid user, bool popMsgs)
    {
        var mapCoordinates = _transform.ToMapCoordinates(_mapSystem.ToCenterCoordinates(gridUid, position, mapGrid));

        if (_ksNoBuildZoneSystem.IsBlocked(mapCoordinates.MapId, out var blockedMessage))
        {
            if (popMsgs)
                _popup.PopupClient(KsPopupMessage.Create(blockedMessage), uid, user);

            return true;
        }

        if (!_ksNoBuildZoneSystem.IsForbidden(mapCoordinates))
            return false;

        if (popMsgs)
            _popup.PopupClient(KsPopupMessage.Create("ks-rcd-no-build-zone"), uid, user);

        return true;
    }
}
