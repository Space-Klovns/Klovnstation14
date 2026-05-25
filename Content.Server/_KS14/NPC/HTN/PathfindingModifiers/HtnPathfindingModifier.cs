namespace Content.Server._KS14.NPC.HTN.PathfindingModifiers;

/// <summary>
///     Applied for MoveToOperator in specific, so that blackboard data can be acted
///         upon when determining paths for the NPCs: specifically, for acting upon
///         the cost of traversing individual tiles in a path.
///
///     Imagine you wanted the NPC to move somewhere while avoiding LOS with X target:
///         one of these would be specified in the MoveToOperator and will take and store, say
///         `XCoordinates` from the blackboard. Later during pathfinding, some member of this
///         will be called during tile cost determination. This modifier will then be able to
///         act on the individual tile cost using the stored XCoordinates and the position of the tile,
///         and find LOS.
///
///     Not implemented yet
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class HtnPathfindingModifier
{
    /// <summary>
    ///     Handles one-time initialization of this instance.
    /// </summary>
    public virtual void Initialize(IEntitySystemManager sysManager)
    {
        sysManager.DependencyCollection.InjectDependencies(this);
    }

    // /// <summary>
    // ///     Handles one-time initialization of this instance.
    // /// </summary>
    // public virtual void Initialize(IEntitySystemManager sysManager)
    // {
    //     sysManager.DependencyCollection.InjectDependencies(this);
    // }
}
