using Content.Server.Explosion.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Explosion.Components;

namespace Content.Server._KS14.Chemistry.Explosion;

/// <summary>
///     Explosions of containers, as strong as what they hold (<see cref="ReagentPrototype.KsExplosivePower"/>), rather than
///         a fixed strength per container.
/// </summary>
/// <remarks>
///     The strength grows with the square root of the explosive power, calibrated so a full fuel tank (960u of welding
///         fuel) explodes at <see cref="ReferenceIntensity"/> 60 and a high-capacity one (3840u) at 120, as they did
///         before. A container's <see cref="ExplosiveComponent"/>, if it has one, still sets the explosion's shape; one
///         without uses <see cref="DefaultExplosionType"/>.
/// </remarks>
public sealed partial class KsSolutionExplosionSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private ExplosionSystem _explosionSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<ExplosiveComponent> _explosiveQuery = default!;

    /// <summary>
    ///     The intensity of an explosion of <see cref="ReferencePower"/>.
    /// </summary>
    public const float ReferenceIntensity = 60f;

    /// <summary>
    ///     A full fuel tank's explosive power: 960u of welding fuel.
    /// </summary>
    public const float ReferencePower = 960f;

    /// <summary>
    ///     Weaker explosions than this are left out: the container only spills. A beaker with a few units of fuel in it
    ///         doesn't blow up.
    /// </summary>
    public const float MinimumIntensity = 1f;

    public const string DefaultExplosionType = "Default";

    /// <summary>
    ///     The total explosive power of a solution: each reagent's quantity times its
    ///         <see cref="ReagentPrototype.KsExplosivePower"/>.
    /// </summary>
    public float GetExplosivePower(Solution solution)
    {
        var power = 0f;
        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (ProtoMan.TryIndex<ReagentPrototype>(reagent.Prototype, out var reagentPrototype))
                power += (float)quantity * reagentPrototype.KsExplosivePower;
        }

        return power;
    }

    public static float GetIntensity(float explosivePower)
    {
        return explosivePower <= 0f ? 0f : ReferenceIntensity * MathF.Sqrt(explosivePower / ReferencePower);
    }

    /// <summary>
    ///     The lowest temperature any explosive reagent in the solution cooks off at, or null if none does.
    /// </summary>
    public float? GetCookOffTemperature(Solution solution)
    {
        float? lowest = null;
        foreach (var (reagent, _) in solution.Contents)
        {
            if (!ProtoMan.TryIndex<ReagentPrototype>(reagent.Prototype, out var reagentPrototype) ||
                reagentPrototype.KsExplosivePower <= 0f ||
                reagentPrototype.KsCookOffTemperature is not { } temperature)
                continue;

            lowest = lowest is { } current ? MathF.Min(current, temperature) : temperature;
        }

        return lowest;
    }

    /// <summary>
    ///     Spills the solution out and explodes it, as strongly as what it holds. Does nothing if it holds nothing that
    ///         explodes, or too little to.
    /// </summary>
    /// <returns>Whether it exploded.</returns>
    public bool TryExplodeSolution(EntityUid uid, string solutionName, EntityUid? cause = null)
    {
        if (!_solutionContainerSystem.TryGetSolution(uid, solutionName, out var solutionEntity, out var solution) ||
            solution.Volume <= 0)
            return false;

        var intensity = GetIntensity(GetExplosivePower(solution));
        if (intensity < MinimumIntensity)
            return false;

        // Spilled first, so the explosion can light it. Taken out of the container rather than spilled from it:
        //      spilling only copies what it's given, and the container's own destruction would then spill it all again.
        var coordinates = Transform(uid).Coordinates;
        var spilled = _solutionContainerSystem.SplitSolution(solutionEntity.Value, solution.Volume);
        _puddleSystem.TrySpillAt(coordinates, spilled, out _);

        // Not deleted here: the explosion's own damage cleans the container up.
        if (_explosiveQuery.TryComp(uid, out var explosiveComponent))
        {
            _explosionSystem.TriggerExplosive(uid, explosiveComponent, delete: false, totalIntensity: intensity, user: cause);
            return true;
        }

        _explosionSystem.QueueExplosion(_transformSystem.ToMapCoordinates(coordinates), DefaultExplosionType, intensity,
            slope: 1f, maxTileIntensity: 4f, cause, tileBreakScale: 0f);
        return true;
    }
}
