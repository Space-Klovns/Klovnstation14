using Content.Shared.FixedPoint;

namespace Content.Shared._Starlight.Plumbing.Components;

/// <summary>
///     A plumbing tank that stores reagents from the network.
///     Actively pulls reagents from its inlet network each update tick.
///     Other machines can pull from this tank via <see cref="PlumbingOutletComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class PlumbingTankComponent : Component
{
    /// <summary>
    ///     The name of the solution on this entity that the tank uses.
    /// </summary>
    [DataField]
    public string SolutionName = "tank";

    /// <summary>
    ///     Name of the inlet node for receiving reagents.
    /// </summary>
    [DataField]
    public string InletName = "inlet";

    /// <summary>
    ///     Name of the outlet node for providing reagents.
    /// </summary>
    [DataField]
    public string OutletName = "outlet";

    /// <summary>
    ///     Amount to transfer per update when pushing to outputs.
    /// </summary>
    [DataField]
    public FixedPoint2 TransferAmount = FixedPoint2.New(10);

    /// <summary>
    ///     Round-robin index for fair outlet selection.
    ///     Tracks which outlet to start from when pulling from multiple sources.
    /// </summary>
    public int RoundRobinIndex;
}
