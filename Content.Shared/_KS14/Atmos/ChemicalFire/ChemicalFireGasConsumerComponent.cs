using Content.Shared.Atmos;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Atmos.ChemicalFire;

/// <summary>
///     Makes a chemfire eat gas off the tile it burns on, every
///         <see cref="ChemicalFireComponent.HeatInterval"/>. Consumption is driven off
///         <see cref="ChemicalFireHeatTileEvent"/>, so it always runs against the same tile the fire heats.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class ChemicalFireGasConsumerComponent : Component
{
    /// <summary>
    ///     Gases this chemfire consumes, and how many moles per second of each.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public Dictionary<Gas, float> Gases = [];

    /// <summary>
    ///     Gases this chemfire produces, as relative shares of whatever it actually consumed - the total moles
    ///         produced always equal the total moles taken off the tile, so only the ratio between entries matters.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<Gas, float>? ProducedGasRatios = null;

    /// <summary>
    ///     Whether the chemfire dies early once none of its <see cref="Gases"/> are left on the tile.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool ExtinguishWhenDepleted = true;
}
