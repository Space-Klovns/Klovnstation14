namespace Content.Shared._Starlight.Plumbing.Components;

/// <summary>
///     Marks an entity as receiving solutions that can be pushed to via the plumbing network.
///     Used by inputs to find any solution receiver in the network.
/// </summary>
[RegisterComponent]
public sealed partial class PlumbingInletComponent : Component
{
    /// <summary>
    ///     The name of the solution to receive into.
    /// </summary>
    [DataField]
    public string SolutionName = "tank";

    /// <summary>
    ///     The name of the node that receives solutions (input side).
    ///     If null, all nodes are considered receivers.
    /// </summary>
    [DataField]
    public string? InletName;
}
