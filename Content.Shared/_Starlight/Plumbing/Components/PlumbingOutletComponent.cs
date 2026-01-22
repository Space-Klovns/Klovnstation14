namespace Content.Shared._Starlight.Plumbing.Components;

/// <summary>
///     Marks an entity as a reagent source on the plumbing network.
///     <see cref="PlumbingPullSystem"/> discovers entities with this component
///     when other machines request reagents from the network.
/// </summary>
[RegisterComponent]
public sealed partial class PlumbingOutletComponent : Component
{
    /// <summary>
    ///     The name of the solution to provide to the network.
    /// </summary>
    [DataField]
    public string SolutionName = "tank";

    /// <summary>
    ///     The name of the node that provides solutions (output side).
    /// </summary>
    [DataField]
    public string? OutletName;
}
