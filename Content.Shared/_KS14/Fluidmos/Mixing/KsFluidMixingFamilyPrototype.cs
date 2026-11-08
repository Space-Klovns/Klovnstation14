using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Fluidmos.Mixing;

/// <summary>
///     A family of reagents that mix with other families alike, so non-ideal mixing needs one number per pair of
///         families rather than per pair of reagents. A reagent joins one through its Fluidmos block's
///         <see cref="KsFluidmosReagentData.MixingFamily"/>; a reagent in none mixes ideally with everything.
/// </summary>
/// <remarks>
///     Interactions are two-suffix Margules parameters <c>A</c> at 298 K, symmetric between the two families, scaled to
///         <c>A · 298 / T</c> at other temperatures. Positive <c>A</c> raises each component's vapor pressure over its
///         ideal share (a minimum-boiling azeotrope when strong enough); negative lowers it.
/// </remarks>
[Prototype]
public sealed partial class KsFluidMixingFamilyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     The Margules <c>A</c> at 298 K between a member of this family and a member of each listed family. A family's
    ///         members mix ideally with each other unless it lists itself.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<KsFluidMixingFamilyPrototype>, float> Interactions = new();

    /// <summary>
    ///     Families whose liquids do not mix with this one's at all, as oil does not with water. Each forms its own liquid
    ///         phase, so each exerts its own full vapor pressure: a mixture of the two boils below either, which is steam
    ///         distillation. Either side listing the other is enough.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<KsFluidMixingFamilyPrototype>> ImmiscibleWith = new();
}
