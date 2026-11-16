using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.LockerMech;

/// <summary>The pilot chooses the tile the locker will fall onto.</summary>
public sealed partial class KsLockerTipActionEvent : WorldTargetActionEvent;

/// <summary>Stores the snapped landing tile for the duration of the wind-up.</summary>
[Serializable, NetSerializable]
public sealed partial class KsLockerTipDoAfterEvent(NetCoordinates landing) : DoAfterEvent
{
    public readonly NetCoordinates Landing = landing;

    public override DoAfterEvent Clone() => new KsLockerTipDoAfterEvent(Landing);
}
