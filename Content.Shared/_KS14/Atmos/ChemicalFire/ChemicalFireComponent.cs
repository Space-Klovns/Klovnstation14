using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.Atmos.ChemicalFire;

/// <summary>
///     A chemical fire ("chemfire"): a static, tile-locked entity that heats the air on the tile it occupies.
///         Chemfires are never spawned directly - <see cref="SharedChemicalFireSystem.SpawnChemicalFire"/> is
///         the only supported entry point, which is why this component implies the (spawn-menu-hidden)
///         <c>ChemicalFire</c> entity category.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
[EntityCategory("ChemicalFire")]
public sealed partial class ChemicalFireComponent : Component
{
    /// <summary>
    ///     How long this chemfire exists for after being spawned or refreshed.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Absolute time at which this chemfire dies. Set from <see cref="Duration"/> on startup and on refresh.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime = TimeSpan.Zero;

    /// <summary>
    ///     Colour the (greyscale) fire sprite and the tile emission are modulated with.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public Color Color = Color.White;

    /// <summary>
    ///     Temperature the tile's hotspot is exposed to. Must exceed
    ///         <see cref="Shared.Atmos.Atmospherics.PlasmaMinimumBurnTemperature"/> to ignite anything.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Temperature = 1500f;

    /// <summary>
    ///     Volume of the exposed hotspot. Bigger values ramp the resulting fire faster.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ExposedVolume = 50f;

    /// <summary>
    ///     How often <see cref="ChemicalFireHeatTileEvent"/> is raised on this chemfire.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan HeatInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     Absolute time of the next heat tick. Not networked - both sides schedule it identically.
    /// </summary>
    [AutoPausedField]
    public TimeSpan NextHeatTime = TimeSpan.Zero;

    /// <summary>
    ///     <see cref="Content.Shared.Light.Components.TileEmissionComponent.Range"/> of the automatically
    ///         added tile emission.
    /// </summary>
    [DataField]
    public float EmissionRange = 0.25f;

    /// <summary>
    ///     Whether putting the tile's fire out - an extinguisher, a water grenade, anything else that ends up
    ///         calling <c>AtmosphereSystem.HotspotExtinguish</c> - also puts this chemfire out.
    ///     Chemfires that are meant to burn through a dousing (thermite, welding fuel and the like) turn it off.
    /// </summary>
    [DataField]
    public bool Extinguishable = true;

    /// <summary>
    ///     Chemfires sharing a connection key smooth into each other laterally, and may not share a tile.
    ///         Null falls back to the entity's prototype id, so distinct prototypes are distinct by default.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? ConnectionKey = null;

    /// <summary>
    ///     Number of <c>-1</c>/<c>-2</c> style sprite variants present in the RSI. The used variant is picked
    ///         deterministically from the grid and tile, so client and server never disagree.
    /// </summary>
    [DataField]
    public int SpriteVariations = 2;

    /// <summary>
    ///     Prefix of the RSI states this chemfire renders, before the variation number.
    /// </summary>
    [DataField]
    public string UnderStatePrefix = "white_under-";

    /// <inheritdoc cref="UnderStatePrefix"/>
    [DataField]
    public string OverStatePrefix = "white_over-";

    /// <summary>
    ///     Grid this chemfire is registered on, cached to keep shutdown deregistration cheap and correct
    ///         even once the transform has already moved on.
    /// </summary>
    [AutoNetworkedField]
    public EntityUid? LocalGridUid = null;

    /// <inheritdoc cref="LocalGridUid"/>
    [AutoNetworkedField]
    public Vector2i LocalTile = Vector2i.Zero;

    /// <summary>
    ///     Client-only, derived: which sprite variation this chemfire uses. Deterministic, so it is never networked.
    /// </summary>
    public int Variation = 1;

    /// <summary>
    ///     Client-only, derived: which lateral neighbours this chemfire smooths into.
    /// </summary>
    public ChemicalFireConnection Connection = ChemicalFireConnection.None;

    /// <summary>
    ///     Client-only, derived: the resolved <c>over</c> RSI state the overlay draws for this chemfire.
    ///         Resolved alongside the <c>under</c> layer so the overlay never has to rebuild the name.
    /// </summary>
    public string OverState = string.Empty;

    /// <summary>
    ///     Client-only: dedup guard so a chemfire's visuals are recalculated at most once per frame.
    /// </summary>
    public int UpdateGeneration = 0;
}

/// <summary>
///     Which lateral neighbours a chemfire connects to, mapping onto the RSI's
///         <c>-west</c>/<c>-east</c>/<c>-full</c> state suffixes.
/// </summary>
[Flags]
public enum ChemicalFireConnection : byte
{
    None = 0,
    West = 1 << 0,
    East = 1 << 1,
    Full = West | East,
}
