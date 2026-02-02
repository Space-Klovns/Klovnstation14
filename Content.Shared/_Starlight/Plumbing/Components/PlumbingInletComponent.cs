using Content.Shared.FixedPoint;

namespace Content.Shared._Starlight.Plumbing.Components;

/// <summary>
///     A plumbing inlet that pulls reagents from the network.
///     Actively pulls reagents from its inlet nodes each update tick into the specified solution.
///     Other machines can pull from this entity via <see cref="PlumbingOutletComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class PlumbingInletComponent : Component
{
    /// <summary>
    ///     The name of the solution on this entity to pull reagents into.
    /// </summary>
    [DataField]
    public string SolutionName = "tank";

    /// <summary>
    ///     The name of the inlet node to pull from.
    /// </summary>
    [DataField]
    public string InletName = "inlet";

    /// <summary>
    ///     Amount to transfer per update.
    /// </summary>
    [DataField]
    public FixedPoint2 TransferAmount = FixedPoint2.New(20);

    /// <summary>
    ///     Round-robin index for fair outlet selection.
    ///     Tracks which outlet to start from when pulling from multiple sources.
    /// </summary>
    public int RoundRobinIndex;
}
