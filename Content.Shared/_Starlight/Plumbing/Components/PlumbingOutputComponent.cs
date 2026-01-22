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
    ///     Name of the inlet node for pulling reagents from the network.
    /// </summary>
    [DataField]
    public string InletName = "duct";

    /// <summary>
    ///     Amount to request per update.
    /// </summary>
    [DataField]
    public FixedPoint2 RequestAmount = FixedPoint2.New(10);

    /// <summary>
    ///     Round-robin index for fair outlet selection.
    ///     Tracks which outlet to start from when pulling from multiple sources.
    /// </summary>
    public int RoundRobinIndex;
}
