using Content.Shared._KS14.Fluidmos;
using Content.Shared.Chemistry.Components;

namespace Content.Server._KS14.Fluidmos;

/// <summary>
///     Fluid a device has released towards its tile, held until there is enough to be worth a puddle or a puff of smoke.
///         Flushed when the device shuts down.
/// </summary>
[RegisterComponent, Access(typeof(KsFluidmosTileSystem))]
public sealed partial class KsFluidmosTileBufferComponent : Component
{
    /// <summary>
    ///     Liquid (and solid) waiting to spill as a puddle.
    /// </summary>
    [DataField]
    public Solution Liquid = new();

    /// <summary>
    ///     Unlinked vapor, as the units it came from, waiting to become smoke.
    /// </summary>
    [DataField]
    public Solution Vapor = new();

    /// <summary>
    ///     What a release held too little of to make the 0.01u a solution stores, carried into the next release rather
    ///         than dropped. A device releasing a little every update would otherwise lose a share of each release.
    /// </summary>
    [ViewVariables]
    public KsFluidMixture? Remainder;
}
