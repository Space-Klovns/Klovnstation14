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
    ///     Prefix for inlet node names. All nodes starting with this prefix will be used for pulling reagents.
    /// </summary>
    [DataField]
    public string InletPrefix = "inlet";

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
