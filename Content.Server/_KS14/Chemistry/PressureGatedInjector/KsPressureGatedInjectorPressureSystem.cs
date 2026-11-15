using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Chemistry.PressureGatedInjector;
using Content.Shared.Atmos;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Chemistry.PressureGatedInjector;

/// <summary>
///     Keeps <see cref="KsPressureGatedInjectorComponent.LowPressure"/> up to date with the air where each injector is.
///         The air around a carried injector changes with no event to say so, so it is checked on an interval.
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
    private float GetPressure(Entity<TransformComponent> injectorEntity)
    {
        var coordinates = _transformSystem.GetMapCoordinates(injectorEntity);
        GasMixture? mixture = _mapSystem.TryFindGridAt(coordinates, out var gridUid, out var gridComponent)
            ? _atmosphereSystem.GetGridTileMixture(gridUid, _mapSystem.WorldToTile(gridUid, gridComponent, coordinates.Position))
            : _atmosphereSystem.GetMapMixture(injectorEntity.Comp.MapUid);

        return mixture?.Pressure ?? 0f;
    }
}
