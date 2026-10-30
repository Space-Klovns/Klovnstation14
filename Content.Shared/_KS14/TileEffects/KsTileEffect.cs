using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Map;

namespace Content.Shared._KS14.TileEffects;

[ImplicitDataDefinitionForInheritors]
public abstract partial class KsTileEffect
{
    [Dependency] protected EntityManager EntityManager = default!;
    [Dependency] private KsTileEffectSystem _tileEffectSystem = default!;

    /// <summary>
    ///     Whether <see cref="Initialize"/> has run on this instance. Not a data field, so every instance a prototype
    ///         load makes starts without it, including ones a reload of some other kind makes (a partial prototype being
    ///         merged again), which <see cref="KsTileEffectSystem"/> is never told about.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    ///     Whether this was injected with the systems <paramref name="tileEffectSystem"/> belongs to. A client makes its
    ///         systems anew on every connection, while its prototypes, and the effects on them, stay: one injected
    ///         before that still holds the old systems, and acts on nothing.
    /// </summary>
    public bool IsInitializedFor(KsTileEffectSystem tileEffectSystem)
        => _initialized && ReferenceEquals(_tileEffectSystem, tileEffectSystem);

    private bool _initialized;

    [MustCallBase]
    public virtual void Initialize(IDependencyCollection dependencyCollection)
    {
        dependencyCollection.InjectDependencies(this);
        _initialized = true;
    }

    // return whether someting happened
    public abstract bool Execute(TileRef tileRef, float scale, ref KsTileEffectContext context);

    public bool Execute(TileRef tileRef, float scale)
    {
        var context = new KsTileEffectContext(0f, null, null);
        return Execute(tileRef, scale, ref context);
    }
}

/// <summary>
///     The state of one run of a solution's tile effects, handed from effect to effect: the solution, the reagent whose
///         effects are running, how much of it they have used up, and what the caller says about the run.
/// </summary>
/// <param name="RemovedVolume">How much of <paramref name="Reagent"/> the effects so far have used up; removed afterwards.</param>
/// <param name="Reagent">The reagent whose effects are running, when they are running for one in a solution.</param>
/// <param name="FlameTemperature">
///     The temperature of an open flame in the solution, such as a lit welder lying in a puddle, if there is one. It
///         lights what <see cref="Effects.CheckTemperatureTileEffect.LitByFlame"/> says it lights, whatever the
///         solution's own temperature.
/// </param>
/// <param name="RefreshedWithin">
///     The longest the caller may take to run these effects again, if it runs them on a schedule. An effect that lasts
///         until it is next refreshed (a chemfire on a burning puddle) lasts this long, so it cannot run out first.
/// </param>
public record struct KsTileEffectContext(
    float RemovedVolume,
    Solution? Solution,
    List<ReagentData>? ReagentData,
    ReagentId? Reagent = null,
    float? FlameTemperature = null,
    TimeSpan? RefreshedWithin = null
);
