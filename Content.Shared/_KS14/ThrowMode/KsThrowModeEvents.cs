using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.ThrowMode;

/// <summary>
///     Sent by a client to turn its throw mode on or off, from the HUD button.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsToggleThrowModeEvent : EntityEventArgs;

/// <summary>
///     Sent by a client in throw mode instead of its click, so the click throws rather than interacts.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsThrowModeThrowEvent(NetCoordinates coordinates) : EntityEventArgs
{
    public readonly NetCoordinates Coordinates = coordinates;
}
