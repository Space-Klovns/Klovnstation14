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
    ///     Produces moles per second by this chemfire.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<Gas, float>? ProducedGases = null;

    /// <summary>
    ///     Whether the chemfire dies early once none of its <see cref="Gases"/> are left on the tile.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool ExtinguishWhenDepleted = true;
}
