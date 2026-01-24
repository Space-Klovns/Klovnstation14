using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Plumbing.Components;

/// <summary>
///     Marks an entity as a reagent source on the plumbing network.
///     <see cref="PlumbingPullSystem"/> discovers entities with this component
///     when other machines request reagents from the network.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
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

    /// <summary>
    ///     If true, this outlet can be pulled from. If false, it's blocked.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>
    ///     If set, look for the solution on the entity in this container slot instead of on this entity.
    ///     Useful for machines like dispensers where the solution is in a beaker.
    /// </summary>
    [DataField]
    public string? ContainerSlotId;
}
