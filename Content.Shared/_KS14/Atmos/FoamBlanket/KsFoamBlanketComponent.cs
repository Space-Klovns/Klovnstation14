using Content.Shared.Atmos;
using Content.Shared.DisplacementMap;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Atmos.FoamBlanket;

/// <summary>
///     A blanket of firefighting foam over a tile. Smothers whatever is burning on the tile when laid or topped up,
///         puts out anything that walks onto it on fire, and keeps chemfires that can be extinguished from starting
///         on the tile for as long as it lasts.
/// </summary>
/// <remarks>
///     It holds <see cref="Foam"/> units of foam, which drain away by themselves and boil off faster where the
///         puddle or air under it is above <see cref="BoilingPoint"/>. Boiling takes its heat from that puddle or
///         air, so a blanket over a hot fuel puddle cools it while it holds, and the fuel can relight once it is gone.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
[Access(typeof(SharedKsFoamBlanketSystem))]
public sealed partial class KsFoamBlanketComponent : Component
{
    /// <summary>
    ///     Units of foam the blanket holds. It is removed once this runs out.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float Foam;

    /// <summary>
    ///     Most units of foam one blanket can hold; spraying more onto a full one uses none.
    /// </summary>
    [DataField]
    public float MaxFoam = 1.5f;

    /// <summary>
    ///     Most units of foam one pass over the tile adds, however much is on offer.
    /// </summary>
    /// <remarks>
    ///     Kept small so that one vapor lays a blanket on every tile it crosses: a vapor offers each tile half of
    ///         what it still carries, so a blanket taking all of it would leave too little for the tiles after it.
    /// </remarks>
    [DataField]
    public float FoamPerTile = 0.1f;

    /// <summary>
    ///     Fewest units a new blanket is laid with; less than this is not enough to lay one.
    ///     Below the 0.05u a vapor always offers a tile, so a vapor with any foam left still lays one.
    /// </summary>
    [DataField]
    public float MinimumFoam = 0.04f;

    /// <summary>
    ///     Units per second the blanket loses however cold it is, so that it does not last forever.
    /// </summary>
    [DataField]
    public float DecayRate = 0.01f;

    /// <summary>
    ///     Most units per second that may boil off over a puddle or air hotter than <see cref="BoilingPoint"/>.
    /// </summary>
    [DataField]
    public float BoilRate = 0.03f;

    /// <summary>
    ///     Temperature, in kelvin, the foam boils at.
    /// </summary>
    [DataField]
    public float BoilingPoint = Atmospherics.T0C + 100f;

    /// <summary>
    ///     Energy, in joules, that boiling a unit takes on top of warming it to <see cref="BoilingPoint"/>.
    ///     Matches <see cref="TileEffects.Effects.BoilTileEffect.LatentHeat"/>, since the foam is mostly water.
    /// </summary>
    [DataField]
    public float LatentHeat = 540f;

    /// <summary>
    ///     Heat capacity of a unit of foam, in J/K, as for a reagent's specific heat.
    /// </summary>
    [DataField]
    public float SpecificHeat = 1f;

    /// <summary>
    ///     Temperature, in kelvin, the foam is laid at.
    /// </summary>
    [DataField]
    public float FoamTemperature = Atmospherics.T20C;

    /// <summary>
    ///     The gas the foam boils into.
    /// </summary>
    [DataField]
    public Gas BoiledGas = Gas.WaterVapor;

    /// <summary>
    ///     How often the blanket drains and boils.
    /// </summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [ViewVariables, AutoPausedField]
    public TimeSpan NextUpdate = TimeSpan.Zero;

    /// <summary>
    ///     How long the blanket spends dissolving once its foam has run out, before it is removed. Matches the
    ///         length of <see cref="DissolveDisplacement"/>'s animation.
    /// </summary>
    [DataField]
    public TimeSpan DissolveDuration = TimeSpan.FromSeconds(0.6);

    /// <summary>
    ///     When the blanket is removed, once its foam has run out and it has started dissolving. Null while it
    ///         holds. A dissolving blanket does nothing: it is not on the tile's cache, so it neither smothers nor
    ///         keeps chemfires out.
    /// </summary>
    [ViewVariables, AutoNetworkedField, AutoPausedField]
    public TimeSpan? DissolveEndTime;

    /// <summary>
    ///     Mask animation the client lays over the blanket's <c>foam</c> layer while it dissolves, whatever art the
    ///         layer has. Client-only; unused on the server.
    /// </summary>
    [DataField]
    public DisplacementData? DissolveDisplacement;

    /// <summary>
    ///     Played wherever the blanket puts something out.
    /// </summary>
    [DataField]
    public SoundSpecifier SmotherSound = new SoundPathSpecifier("/Audio/_KS14/Effects/burn_sizzle.ogg");

    /// <summary>
    ///     Grid the blanket is registered on, cached so it can be deregistered once the transform has moved on.
    /// </summary>
    [ViewVariables]
    public EntityUid? LocalGridUid;

    /// <inheritdoc cref="LocalGridUid"/>
    [ViewVariables]
    public Vector2i LocalTile;
}

/// <summary>
///     The foam blankets on a grid, by tile, so that a chemfire's sustain check finds one without an entity lookup.
///     Built on each side from the blankets' own startup and shutdown, so it is never networked.
/// </summary>
[RegisterComponent]
[Access(typeof(SharedKsFoamBlanketSystem), Other = AccessPermissions.Read)]
public sealed partial class KsFoamBlanketGridComponent : Component
{
    [ViewVariables]
    public readonly Dictionary<Vector2i, EntityUid> Tiles = [];
}
