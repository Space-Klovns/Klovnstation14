using System.Numerics;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.FieldCommander;

/// <summary>
///     On a map: a circular play area. Outside <see cref="Range"/> the client draws a fog, and the server hurts the
///         defenders standing in it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsFieldCommanderFogComponent : Component
{
    /// <summary>
    ///     Radius of the clear area, in tiles.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Range = 78f;

    /// <summary>
    ///     Centre of the clear area, in map-local coordinates.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 Origin;

    /// <summary>
    ///     How many tiles inside <see cref="Range"/> the warnings begin.
    /// </summary>
    [DataField(serverOnly: true)]
    public float WarningMargin = 6f;

    /// <summary>
    ///     Damage per second at the edge of the fog.
    /// </summary>
    [DataField(serverOnly: true)]
    public DamageSpecifier DamagePerSecond = new()
    {
        DamageDict = { ["Cold"] = 2 },
    };

    /// <summary>
    ///     The damage doubles for every this many tiles past the edge.
    /// </summary>
    [DataField(serverOnly: true)]
    public float DoublingDistance = 10f;

    /// <summary>
    ///     Cap on the multiplier of <see cref="DamagePerSecond"/>.
    /// </summary>
    [DataField(serverOnly: true)]
    public float MaxDamageScale = 6f;

    /// <summary>
    ///     Only members of this faction are affected: the side besieged, set by whatever raised the fog.
    /// </summary>
    [DataField(serverOnly: true)]
    public ProtoId<ScenarioFactionPrototype> Faction = "Nanotrasen";
}
