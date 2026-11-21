using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.RCD;

/// <summary>
///     On a map: where an RCD can't be used at all. Circles, such as a game mode's spawn area, and a switch for the
///         whole map. Networked, so the client predicts the refusal and the RCD's placement ghost shows it as
///         impossible before anyone tries.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(KsNoBuildZoneSystem))]
public sealed partial class KsNoBuildZonesComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<KsNoBuildZone> Zones = [];

    /// <summary>
    ///     When set, an RCD can't be used anywhere on the map, and this is what whoever tries is told.
    /// </summary>
    /// <seealso cref="KsNoBuildZoneSystem.SetBlocked"/>
    [DataField, AutoNetworkedField]
    public LocId? BlockedMessage;
}

/// <summary>
///     A circle, in map coordinates.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public partial record struct KsNoBuildZone
{
    [DataField]
    public Vector2 Center;

    [DataField]
    public float Radius;

    public KsNoBuildZone(Vector2 center, float radius)
    {
        Center = center;
        Radius = radius;
    }
}
