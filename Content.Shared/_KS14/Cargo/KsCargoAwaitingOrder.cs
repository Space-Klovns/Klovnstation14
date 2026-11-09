using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cargo;

/// <summary>
///     Where an approved order's goods wait to be collected.
/// </summary>
[Serializable, NetSerializable]
public enum KsCargoDeliveryPoint : byte
{
    TradeStation,
    Telepad,
}

/// <summary>
///     One approved order whose goods are still waiting for pickup, at one kind of delivery point, as the cargo console's
///         checkout lists it.
/// </summary>
/// <param name="OrderId">The order's id.</param>
/// <param name="Product">The ordered product.</param>
/// <param name="Requester">Who asked for it.</param>
/// <param name="Approver">Who approved it, if known.</param>
/// <param name="Account">The account that paid.</param>
/// <param name="Where">The kind of delivery point.</param>
/// <param name="OnPad">How many of the goods are on the pallet or telepad now.</param>
/// <param name="Queued">How many a telepad has yet to teleport in.</param>
[Serializable, NetSerializable]
public sealed record KsCargoAwaitingOrder(
    int OrderId,
    ProtoId<CargoProductPrototype> Product,
    string Requester,
    string? Approver,
    ProtoId<CargoAccountPrototype> Account,
    KsCargoDeliveryPoint Where,
    int OnPad,
    int Queued)
{
    /// <summary>
    ///     Everything of this order that is still to be picked up.
    /// </summary>
    public int Total => OnPad + Queued;
}
