using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Chemistry.InfiniteDispenser;

/// <summary>
///     Asks a dispenser with a <see cref="KsInfiniteReagentDispenserComponent"/> to make its dispense amount of
///         <see cref="Reagent"/> in the beaker.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsReagentDispenserDispenseInfiniteMessage(ProtoId<ReagentPrototype> reagent) : BoundUserInterfaceMessage
{
    public readonly ProtoId<ReagentPrototype> Reagent = reagent;
}
