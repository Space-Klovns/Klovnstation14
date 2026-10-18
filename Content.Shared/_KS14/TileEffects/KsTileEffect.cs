using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;

namespace Content.Shared._KS14.TileEffects;

[ImplicitDataDefinitionForInheritors]
public abstract partial class KsTileEffect
{
    [Dependency] protected EntityManager EntityManager = default!;

    [MustCallBase]
    public virtual void Initialize(IDependencyCollection dependencyCollection)
    {
        dependencyCollection.InjectDependencies(this);
    }

    // return whether someting happened
    public abstract bool Execute(TileRef tileRef, float scale, ref KsTileEffectReagentData reagentData);

    public bool Execute(TileRef tileRef, float scale)
    {
        var reagentData = new KsTileEffectReagentData(0f, null, null);
        return Execute(tileRef, scale, ref reagentData);
    }
}

/// <param name="RemovedVolume">How much of <paramref name="Reagent"/> the effects so far have used up; removed afterwards.</param>
/// <param name="Reagent">The reagent whose effects are running, when they are running for one in a solution.</param>
public record struct KsTileEffectReagentData(float RemovedVolume, Solution? Solution, List<ReagentData>? ReagentData, ReagentId? Reagent = null);
