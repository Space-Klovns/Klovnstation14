using Content.Shared.FixedPoint;

namespace Content.Shared.Chemistry.Reagent;

public sealed partial class ReagentPrototype
{
    /// <summary>
    ///     If set and lower than a puddle's own <c>PuddleComponent.OverflowVolume</c>, a puddle of this reagent alone
    ///         overflows onto neighbouring tiles above this volume instead. A mixture moves towards it by this reagent's
    ///         share of the puddle. A low value spreads the reagent thin and far.
    /// </summary>
    /// <seealso cref="Content.Shared.Fluids.SharedPuddleSystem.KsGetOverflowVolume"/>
    [DataField("ksSpreadVolume")]
    public FixedPoint2? KsSpreadVolume;
}
