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
    ///     Prefix for node names that are valid outlets.
    ///     Only nodes whose names start with this prefix (case-insensitive) will provide reagents.
    ///     For example, "outlet" matches "outlet", "outletSouth", "outletEast", etc.
    /// </summary>
    [DataField]
    public string OutletPrefix = "outlet";
}
