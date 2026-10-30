namespace Content.Server.NPC.Queries.Queries;

/// <summary>
/// Adds entities to a query.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class UtilityQuery
{
    // KS14 start: custom queries, like custom considerations, add their own entities rather than being hardcoded in NPCUtilitySystem
    /// <summary>
    ///     Called when prototypes are reloaded, or this is initialised.
    /// </summary>
    [MustCallBase]
    public virtual void Initialise(IDependencyCollection dependencyCollection) => dependencyCollection.InjectDependencies(this, oneOff: true);

    /// <summary>
    ///     Adds this query's entities to <paramref name="entities"/>. Custom queries override this.
    /// </summary>
    public virtual void AddEntities(NPCBlackboard blackboard, EntityUid ownerUid, HashSet<EntityUid> entities) => throw new NotImplementedException();
    // KS14 end
}
