using Content.Shared.FixedPoint;

namespace Content.Shared.Chemistry.Reagent;

public sealed partial class ReagentPrototype
{
    /// <summary>
    ///     If set, a puddle holding this reagent overflows onto neighbouring tiles above this volume rather than its own
    ///         <c>PuddleComponent.OverflowVolume</c>, if that is lower. A low value spreads the reagent thin and far.
    /// </summary>
    /// <seealso cref="Content.Shared.Fluids.SharedPuddleSystem.KsGetOverflowVolume"/>
    [DataField("ksSpreadVolume")]
    public FixedPoint2? KsSpreadVolume;
}
