using Content.Server._KS14.TileEffects;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Atmos.FoamBlanket;

/// <summary>
///     Drains and boils foam blankets off. Laying and smothering is in the shared system; this supplies the atmos and
///         flammable state only the server has.
/// </summary>
public sealed partial class KsFoamBlanketSystem : SharedKsFoamBlanketSystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsTileEffectAtmosSystem _tileEffectAtmosSystem = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    protected override bool IsBurning(Entity<FlammableComponent> flammable)
        => flammable.Comp.OnFire && flammable.Comp.CanExtinguish;

    protected override bool SmotherTileAtmos(EntityUid gridUid, Vector2i tile)
    {
        if (!_atmosphereSystem.IsHotspotActive(gridUid, tile))
            return false;

        _atmosphereSystem.HotspotExtinguish(gridUid, tile);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;

        var enumerator = EntityQueryEnumerator<KsFoamBlanketComponent>();
        while (enumerator.MoveNext(out var uid, out var blanketComponent))
        {
            if (blanketComponent.DissolveEndTime is { } dissolveEndTime)
            {
                if (curTime >= dissolveEndTime)
                    QueueDel(uid);

                continue;
            }

            if (curTime < blanketComponent.NextUpdate)
                continue;

            blanketComponent.NextUpdate += blanketComponent.UpdateInterval;

            var seconds = (float)blanketComponent.UpdateInterval.TotalSeconds;
            blanketComponent.Foam -= blanketComponent.DecayRate * seconds;
            Boil((uid, blanketComponent), seconds);

            if (blanketComponent.Foam <= 0f)
                // Dissolves rather than vanishing; removed once the dissolve has played.
                StartDissolving((uid, blanketComponent));
            else
                // Every update rather than in steps: blankets are few and update once a second, and the client's
                //     fade follows the foam level.
                Dirty(uid, blanketComponent);
        }
    }

    /// <summary>
    ///     Boils foam off into the tile's air, taking the heat from the puddle under the blanket if that is hotter
    ///         than the foam's boiling point, or from the air if that is.
    /// </summary>
    /// <remarks>
    ///     Boiling a unit takes <c>c (T_boil - T_foam) + L</c> from the source, warming the foam from what it was
    ///         laid at and then boiling it, and puts the unit into the air as gas carrying <c>c T_boil</c>. The
    ///         latent heat leaves with the vapor, as <see cref="Content.Server.Chemistry.TileReactions.ExtinguishTileReaction"/>
    ///         and <see cref="Shared._KS14.TileEffects.Effects.BoilTileEffect"/> account it. The source is never
    ///         cooled below the boiling point, and nothing boils on a tile with no air to take the gas.
    /// </remarks>
    private void Boil(Entity<KsFoamBlanketComponent> blanket, float seconds)
    {
        if (blanket.Comp.Foam <= 0f || blanket.Comp.LocalGridUid is not { } gridUid)
            return;

        var component = blanket.Comp;
        var tile = component.LocalTile;

        if (_atmosphereSystem.GetTileMixture((gridUid, null, null), null, tile, excite: false) is not { Immutable: false } mixture)
            return;

        var energyPerUnit = component.SpecificHeat * (component.BoilingPoint - component.FoamTemperature) + component.LatentHeat;
        if (energyPerUnit <= 0f)
            return;

        var maxBoiled = MathF.Min(component.BoilRate * seconds, component.Foam);

        // The puddle first: it is what the blanket lies on, and what has to be kept from relighting.
        if (_puddleSystem.TryGetCachedPuddle(gridUid, tile, out Entity<PuddleComponent> puddle) &&
            _solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution) &&
            puddle.Comp.Solution is { } solutionEntity &&
            solution.Temperature > component.BoilingPoint)
        {
            var boiled = MathF.Min(maxBoiled,
                (solution.Temperature - component.BoilingPoint) * solution.GetHeatCapacity(ProtoMan) / energyPerUnit);

            if (boiled > 0f && AddBoiledGas(blanket, gridUid, tile, boiled))
            {
                _solutionContainerSystem.AddThermalEnergy(solutionEntity, -boiled * energyPerUnit);
                return;
            }
        }

        if (mixture.Temperature <= component.BoilingPoint)
            return;

        var airHeatCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        if (airHeatCapacity < Atmospherics.MinimumHeatCapacity)
            return;

        var airBoiled = MathF.Min(maxBoiled, (mixture.Temperature - component.BoilingPoint) * airHeatCapacity / energyPerUnit);
        if (airBoiled <= 0f)
            return;

        // The air pays before the gas joins it, so the gas is added at the energy it actually carries.
        mixture.Temperature -= airBoiled * energyPerUnit / airHeatCapacity;
        AddBoiledGas(blanket, gridUid, tile, airBoiled);
    }

    private bool AddBoiledGas(Entity<KsFoamBlanketComponent> blanket, EntityUid gridUid, Vector2i tile, float boiled)
    {
        var component = blanket.Comp;
        if (!_tileEffectAtmosSystem.TryAddBoiledGas(gridUid, tile, component.BoiledGas, boiled, boiled * component.SpecificHeat * component.BoilingPoint))
            return false;

        component.Foam -= boiled;
        return true;
    }

}
