using Content.Shared.FixedPoint;

namespace Content.Shared._Starlight.Plumbing.Components;

/// <summary>
///     A plumbing output that players can draw reagents from using containers.
///     Actively pulls reagents from the network into its buffer each update tick.
/// </summary>
[RegisterComponent]
public sealed partial class PlumbingOutputComponent : Component
{
    /// <summary>
    ///     The name of the solution on this entity that provides output.
    /// </summary>
    [DataField]
    public string SolutionName = "output";

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
