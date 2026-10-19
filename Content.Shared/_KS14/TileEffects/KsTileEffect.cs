using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;

namespace Content.Shared._KS14.TileEffects;

[ImplicitDataDefinitionForInheritors]
public abstract partial class KsTileEffect
{
    [Dependency] protected EntityManager EntityManager = default!;

    /// <summary>
    ///     Whether <see cref="Initialize"/> has run on this instance. Not a data field, so every instance a prototype
    ///         load makes starts without it, including ones a reload of some other kind makes (a partial prototype being
    ///         merged again), which <see cref="KsTileEffectSystem"/> is never told about.
    /// </summary>
    public bool IsInitialized => _initialized;

    private bool _initialized;

    [MustCallBase]
    public virtual void Initialize(IDependencyCollection dependencyCollection)
    {
        dependencyCollection.InjectDependencies(this);
        _initialized = true;
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
