using Content.Shared.EntityTable.EntitySelectors;

namespace Content.Shared.Containers;

/// <summary>
/// Version of <see cref="ContainerFillComponent"/> that utilizes <see cref="EntityTableSelector"/>
/// </summary>
[RegisterComponent, Access(typeof(ContainerFillSystem))]
public sealed partial class EntityTableContainerFillComponent : Component
{
    [DataField]
    public Dictionary<string, EntityTableSelector> Containers = new();

    // KS14 start: port of upstream #44134
    /// <summary>
    /// Whether to sort the contents of the table by size before inserting.
    /// Helps with fitting items into containers.
    /// </summary>
    [DataField]
    public bool Sort;
    // KS14 end
}
