namespace Content.Shared.Construction;

public sealed partial class ConstructionGraphNode
{
    /// <summary>
    ///     If true, then the containers of the used entities
    ///         will be transferred to the new entity, if they exist on the new entity..
    /// </summary>
    [DataField("preserveContainers")]
    public bool PreserveContainers = false;
}
