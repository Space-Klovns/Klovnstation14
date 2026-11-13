using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.RCD;

/// <summary>
///     On a map: circles where an RCD can't be used at all, such as a game mode's spawn area. Networked, so the
///         RCD's placement ghost shows it as impossible before anyone tries.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(KsNoBuildZoneSystem))]
public sealed partial class KsNoBuildZonesComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<KsNoBuildZone> Zones = [];
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
