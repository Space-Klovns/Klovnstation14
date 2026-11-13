using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared._KS14.Chemistry.PressureGatedInjector;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Chemistry.PressureGatedInjector;

/// <summary>
///     Keeps <see cref="KsPressureGatedInjectorComponent.LowPressure"/> up to date with the air where each injector is.
/// </summary>
public sealed partial class KsPressureGatedInjectorPressureSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextCheck)
            return;

        _nextCheck = _gameTiming.CurTime + CheckInterval;

        var injectorEnumerator = EntityQueryEnumerator<KsPressureGatedInjectorComponent, TransformComponent>();
        while (injectorEnumerator.MoveNext(out var injectorUid, out var injectorComponent, out var transformComponent))
        {
            var lowPressure = GetPressure((injectorUid, transformComponent)) <= injectorComponent.MaxPressure;
            if (lowPressure == injectorComponent.LowPressure)
                continue;

            injectorComponent.LowPressure = lowPressure;
            Dirty(injectorUid, injectorComponent);
        }
    }

    /// <summary>
    ///     The pressure of the tile the injector is on, wherever it is carried. Nothing to breathe counts as none.
    /// </summary>
    private float GetPressure(Entity<TransformComponent> injector)
    {
        var mapUid = injector.Comp.MapUid;
        var coordinates = _transformSystem.GetMapCoordinates(injector);
        GasMixture? mixture;
        if (_mapSystem.TryFindGridAt(coordinates, out var gridUid, out var gridComponent))
            mixture = _atmosphereSystem.GetTileMixture(gridUid, mapUid, _mapSystem.WorldToTile(gridUid, gridComponent, coordinates.Position));
        else
            mixture = _atmosphereSystem.GetTileMixture(null, mapUid, Vector2i.Zero);

        return mixture?.Pressure ?? 0f;
    }
}
