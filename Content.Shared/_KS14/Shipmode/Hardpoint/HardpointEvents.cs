using Robust.Shared.Serialization;

namespace Content.Shared._KS14.ShipMode.Hardpoint;

public sealed class HardpointCannonAnchoredEvent : EntityEventArgs
{
    public EntityUid cannonUid;
    public EntityUid gridUid;
}

public sealed class HardpointCannonDeanchoredEvent : EntityEventArgs
{
    public EntityUid CannonUid;
    public EntityUid gridUid;
}
