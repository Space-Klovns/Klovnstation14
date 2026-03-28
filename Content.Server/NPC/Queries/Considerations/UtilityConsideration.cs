using Content.Server.NPC.Queries.Curves;
using JetBrains.Annotations;

namespace Content.Server.NPC.Queries.Considerations;

[ImplicitDataDefinitionForInheritors, MeansImplicitUse]
public abstract partial class UtilityConsideration
{
    [Dependency] protected readonly EntityManager EntityManager = default!; // KS14: ANK

    [DataField("curve", required: true)]
    public IUtilityCurve Curve = default!;

    // KS14: ANK
    /// <summary>
    ///     Called when prototypes are reloaded, or this is initialised.
    /// </summary>
    public virtual void Initialise(IDependencyCollection dependencyCollection) => dependencyCollection.InjectDependencies(this);

    // KS14: ANK
    public virtual float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid) => throw new NotImplementedException();
}
