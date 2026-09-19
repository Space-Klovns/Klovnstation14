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

public record struct KsTileEffectReagentData(float RemovedVolume, Solution? Solution, List<ReagentData>? ReagentData);
