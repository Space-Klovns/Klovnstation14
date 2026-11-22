using Content.Shared._KS14.Perks;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Vendors;

public sealed partial record CMVendorEntry
{
    /// <summary>
    ///     A perk the player must hold to take this entry; others see it greyed out.
    /// </summary>
    [DataField]
    public ProtoId<KsPerkPrototype>? KsRequiredPerk;
}
