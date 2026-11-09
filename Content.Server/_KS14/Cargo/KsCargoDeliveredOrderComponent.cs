using Content.Server.Cargo.Systems;
using Content.Shared.Cargo;

namespace Content.Server._KS14.Cargo;

/// <summary>
///     On the goods an order spawned at a trade-station buying pallet or a cargo telepad, for as long as they stay on that
///         tile. The cargo console's checkout lists what carries it. It is transient state and not a datafield, so a saved
///         map does not keep it.
/// </summary>
[RegisterComponent, Access(typeof(CargoSystem))]
public sealed partial class KsCargoDeliveredOrderComponent : Component
{
    /// <summary>
    ///     The order the goods are for.
    /// </summary>
    [ViewVariables]
    public CargoOrderData Order = default!;

    /// <summary>
    ///     The pallet or telepad the goods were spawned on.
    /// </summary>
    [ViewVariables]
    public EntityUid DeliveryPoint;

    /// <summary>
    ///     The station that delivery point belongs to.
    /// </summary>
    [ViewVariables]
    public EntityUid Station;

    /// <summary>
    ///     The grid the goods were spawned on, and the tile they must stay on to count as waiting.
    /// </summary>
    [ViewVariables]
    public EntityUid Grid;

    [ViewVariables]
    public Vector2i Tile;

    [ViewVariables]
    public bool AtTelepad;
}
