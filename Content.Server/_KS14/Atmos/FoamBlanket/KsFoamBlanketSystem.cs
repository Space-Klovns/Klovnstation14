using Content.Server._KS14.TileEffects;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.Temperature;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Content.Shared.Popups;
using Content.Shared.Temperature.HeatContainer;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Atmos.FoamBlanket;

/// <summary>
///     Drains and boils foam blankets off. Laying and smothering is in the shared system; this supplies the atmos and
///         flammable state only the server has, and runs the fight between foam and a chemfire that resists it.
/// </summary>
public sealed partial class KsFoamBlanketSystem : SharedKsFoamBlanketSystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsTileEffectAtmosSystem _tileEffectAtmosSystem = default!;
    [Dependency] private PuddleSystem _puddleSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    [Dependency] private EntityQuery<KsFoamContestComponent> _contestQuery = default!;

    /// <summary>
    ///     How often a chemfire burning on under foam may play its <see cref="ChemicalFireComponent.FoamResistSound"/>:
    ///         about the length of the default sound, so that it roars on without piling up.
    /// </summary>
    private static readonly TimeSpan ResistSoundInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How often foam landing on a chemfire it does not put out at once may say so.
    /// </summary>
    private static readonly TimeSpan PopupInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     The area, in m², of a tile's floor, which a blanket covers and radiates from.
    /// </summary>
    private const float TileArea = 1f;

    protected override bool IsBurning(Entity<FlammableComponent> flammable)
        => flammable.Comp.OnFire && flammable.Comp.CanExtinguish;

    /// <remarks>
    ///     Only while the air is cooler than the foam's boiling point (<see cref="HoldsBackGasFire"/>). A gas fire whose
    ///         air is hotter burns in the air itself, which foam on the floor does nothing about: putting its flame out
    ///         would only hide it until the air lit it again.
    /// </remarks>
    protected override bool SmotherTileAtmos(Entity<KsFoamBlanketComponent> blanket, EntityUid gridUid, Vector2i tile)
    {
        // Not IsHotspotActive: chemfires answer that for their tiles too, which would sizzle over one the foam cannot
        //     touch. The chemfires on the tile are the shared system's to deal with.
        if (!_atmosphereSystem.HasGasHotspot(gridUid, tile) ||
            !HoldsBackGasFire(blanket.Comp, _atmosphereSystem.GetTileMixture((gridUid, null, null), null, tile, excite: false)))
            return false;

        _atmosphereSystem.HotspotExtinguish(gridUid, tile);
        return true;
    }

    /// <summary>
    ///     Whether the blanket keeps gas from catching fire, and what is in it from burning: only while the air over it
    ///         is cooler than the foam's boiling point.
    /// </summary>
    /// <remarks>
    ///     Above it, the foam is boiling, and the gas burns by itself: plasma burns from 373 K, the foam's boiling point,
    ///         without any flame to light it. So a blanket can stop a spark, or a fire spreading onto cool air, but
    ///         not a fire that has heated the air it burns in. Venting or carbon dioxide is for that.
    ///     A tile with no air, or air that does not change (space), has nothing to burn, so it does not matter.
    /// </remarks>
    private static bool HoldsBackGasFire(KsFoamBlanketComponent component, GasMixture? air)
        => air == null || air.Temperature < component.BoilingPoint;

    /// <summary>
    ///     Whether a foam blanket on the tile keeps a gas fire from starting there. See <see cref="HoldsBackGasFire"/>.
    /// </summary>
    public bool SmothersIgnition(EntityUid gridUid, Vector2i tile, GasMixture? air)
        => TryGetBlanket(gridUid, tile, out var blanket) && HoldsBackGasFire(blanket.Comp, air);

    protected override bool ProtectsOccupants(Entity<KsFoamBlanketComponent> blanket)
    {
        if (blanket.Comp.LocalGridUid is not { } gridUid)
            return true;

        return HoldsBackGasFire(blanket.Comp, _atmosphereSystem.GetTileMixture((gridUid, null, null), null, blanket.Comp.LocalTile, excite: false));
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
            SetFoam((uid, blanketComponent), blanketComponent.Foam - blanketComponent.DecayRate * seconds);
            Boil((uid, blanketComponent), seconds);
            BoilFromRadiatedFire((uid, blanketComponent), seconds);

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
    ///     Trades heat with the puddle under the blanket and the air over it, and boils foam off into the tile's air
    ///         with what takes it past its boiling point: the puddle's heat if there is a puddle hotter than that, or
    ///         else the air's if that is.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The puddle conducts into the foam (<see cref="ExchangeHeatWithPuddle"/>), and the foam into the air, by
    ///             conduction and radiation (<see cref="ExchangeHeatWithAir"/>), so a blanket cools a puddle below its
    ///             boiling point too, and room air cools the blanket, slowly.
    ///     </para>
    ///     <para>
    ///         Boiling a unit takes <c>c (T_boil - T_foam) + L</c>, and puts the unit into the air as gas carrying
    ///             <c>c T_boil</c>. The latent heat leaves with the vapor, as
    ///             <see cref="Content.Server.Chemistry.TileReactions.ExtinguishTileReaction"/> and
    ///             <see cref="Shared._KS14.TileEffects.Effects.BoilTileEffect"/> account it. Nothing boils on a tile with
    ///             no air to take the gas.
    ///     </para>
    /// </remarks>
    private void Boil(Entity<KsFoamBlanketComponent> blanket, float seconds)
    {
        if (blanket.Comp.Foam <= 0f || blanket.Comp.LocalGridUid is not { } gridUid)
            return;

        var component = blanket.Comp;
        var tile = component.LocalTile;

        if (_atmosphereSystem.GetTileMixture((gridUid, null, null), null, tile, excite: false) is not { Immutable: false } mixture)
            return;

        BoilSuperheatedFoam(blanket, gridUid, tile);
        if (component.Foam <= 0f)
            return;

        // The puddle first: it is what the blanket lies on, and what has to be kept from relighting.
        if (ExchangeHeatWithPuddle(blanket, gridUid, tile, seconds) || component.Foam <= 0f)
            return;

        ExchangeHeatWithAir(blanket, mixture, seconds);

        if (mixture.Temperature <= component.BoilingPoint)
            return;

        var energyPerUnit = GetEnergyPerUnit(component);

        var airHeatCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        if (airHeatCapacity < Atmospherics.MinimumHeatCapacity)
            return;

        var airBoiled = MathF.Min(MathF.Min(GetAirBoilRate(component, mixture.Temperature) * seconds, component.Foam),
            (mixture.Temperature - component.BoilingPoint) * airHeatCapacity / energyPerUnit);
        if (airBoiled <= 0f)
            return;

        // The air pays before the gas joins it, so the gas is added at the energy it actually carries.
        mixture.Temperature -= airBoiled * energyPerUnit / airHeatCapacity;
        AddBoiledGas(blanket, gridUid, tile, airBoiled);
    }

    /// <summary>
    ///     Boils off, at once, as much of foam above its boiling point as its heat over that point boils, leaving the
    ///         rest at the boiling point: foam laid hotter than that cannot lie there as liquid.
    /// </summary>
    private void BoilSuperheatedFoam(Entity<KsFoamBlanketComponent> blanket, EntityUid gridUid, Vector2i tile)
    {
        var component = blanket.Comp;
        if (component.Heat.Temperature <= component.BoilingPoint || component.LatentHeat <= 0f)
            return;

        var excessHeat = (component.Heat.Temperature - component.BoilingPoint) * component.Heat.HeatCapacity;
        var boiled = MathF.Min(component.Foam, excessHeat / component.LatentHeat);
        if (!AddBoiledGas(blanket, gridUid, tile, boiled) || component.Foam <= 0f)
            return;

        component.Heat = new HeatContainer(component.Heat.HeatCapacity, component.BoilingPoint);
    }

    /// <summary>
    ///     Conducts heat between the blanket and the puddle under it, if there is one, and boils off foam with what
    ///         would take it past its boiling point.
    /// </summary>
    /// <remarks>
    ///     The foam's capacity is tiny against <see cref="KsFoamBlanketComponent.PuddleConductance"/>, so a puddle hotter
    ///         than the boiling point brings it there almost at once. From then it holds there, and what flows on boils
    ///         it, at most <see cref="KsFoamBlanketComponent.PuddleBoilRate"/>: the puddle pays only for what boils.
    /// </remarks>
    /// <returns>Whether any foam boiled.</returns>
    private bool ExchangeHeatWithPuddle(Entity<KsFoamBlanketComponent> blanket, EntityUid gridUid, Vector2i tile, float seconds)
    {
        var component = blanket.Comp;
        if (!_puddleSystem.TryGetCachedPuddle(gridUid, tile, out Entity<PuddleComponent> puddle) ||
            !_solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var solution) ||
            puddle.Comp.Solution is not { } solutionEntity)
            return false;

        var puddleCapacity = solution.GetHeatCapacity(ProtoMan);
        if (puddleCapacity <= 0f)
            return false;

        var foamHeat = component.Heat;
        var toFoam = KsThermalTransfer.ExchangeQuery(foamHeat.HeatCapacity, foamHeat.Temperature,
            puddleCapacity, solution.Temperature, component.PuddleConductance, seconds);

        var boiled = 0f;
        if (foamHeat.Temperature + toFoam / foamHeat.HeatCapacity > component.BoilingPoint)
        {
            // Warmed to its boiling point, then held there while the puddle conducts into it.
            toFoam = MathF.Max(0f, (component.BoilingPoint - foamHeat.Temperature) * foamHeat.HeatCapacity);
            var boilingHeat = -KsThermalTransfer.ExchangeWithReservoirQuery(puddleCapacity, solution.Temperature - toFoam / puddleCapacity,
                component.BoilingPoint, component.PuddleConductance, seconds);

            boiled = MathF.Min(MathF.Min(component.PuddleBoilRate * seconds, component.Foam), boilingHeat / component.LatentHeat);
            if (boiled <= 0f || !AddBoiledGas(blanket, gridUid, tile, boiled))
                boiled = 0f;
        }

        HeatContainerHelpers.AddHeat(ref component.Heat, toFoam);

        var fromPuddle = toFoam + boiled * component.LatentHeat;
        if (fromPuddle != 0f)
            _solutionContainerSystem.AddThermalEnergy(solutionEntity, -fromPuddle);

        return boiled > 0f;
    }

    /// <summary>
    ///     Conducts and radiates heat between the blanket and the air over it, without taking the foam past its
    ///         boiling point: air hot enough to boil it does so at the rates in <see cref="Boil"/>.
    /// </summary>
    private void ExchangeHeatWithAir(Entity<KsFoamBlanketComponent> blanket, GasMixture mixture, float seconds)
    {
        var component = blanket.Comp;
        var airCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        if (airCapacity < Atmospherics.MinimumHeatCapacity)
            return;

        var foamHeat = component.Heat;
        var conductance = component.AirConductance +
            KsThermalTransfer.RadiativeConductance(foamHeat.Temperature, mixture.Temperature, TileArea, component.Emissivity);

        var toFoam = MathF.Min(
            KsThermalTransfer.ExchangeQuery(foamHeat.HeatCapacity, foamHeat.Temperature, airCapacity, mixture.Temperature, conductance, seconds),
            (component.BoilingPoint - foamHeat.Temperature) * foamHeat.HeatCapacity);
        if (toFoam == 0f)
            return;

        HeatContainerHelpers.AddHeat(ref component.Heat, toFoam);
        mixture.Temperature -= toFoam / airCapacity;
    }

    /// <summary>
    ///     How fast foam may boil off into air at <paramref name="airTemperature"/>: <see cref="KsFoamBlanketComponent.FireBoilRate"/>
    ///         per 1000 K above the boiling point, but never less than <see cref="KsFoamBlanketComponent.BoilRate"/>.
    /// </summary>
    /// <remarks>
    ///     With the default numbers the two agree 100 K above the boiling point, so foam over air up to that is unchanged,
    ///         and only air hotter than that, as a gas fire's is, boils it faster.
    /// </remarks>
    private static float GetAirBoilRate(KsFoamBlanketComponent component, float airTemperature)
        => MathF.Max(component.BoilRate, component.FireBoilRate * (airTemperature - component.BoilingPoint) / 1000f);

    /// <summary>
    ///     A full gas fire on a neighbouring tile is radiating into the blanket here, if there is one. Remembers each such
    ///         neighbour, at its hottest since the last update, which <see cref="BoilFromRadiatedFire"/> then boils the foam
    ///         with.
    /// </summary>
    /// <remarks>
    ///     Only remembered here, since a fire radiates once per atmos cycle into each neighbour, and how often that is
    ///         depends on the server's load. Boiling on the blanket's own update keeps the rate per second.
    /// </remarks>
    public void ExposeToRadiatedFire(EntityUid gridUid, Vector2i tile, Vector2i burningTile, float burningTemperature)
    {
        if (!TryGetBlanket(gridUid, tile, out var blanket))
            return;

        var radiatingTiles = blanket.Comp.RadiatingTiles;
        if (!radiatingTiles.TryGetValue(burningTile, out var temperature) || burningTemperature > temperature)
            radiatingTiles[burningTile] = burningTemperature;
    }

    /// <summary>
    ///     Boils foam off with heat from each gas fire that has radiated into the blanket since the last update, at the
    ///         rate <see cref="GetAirBoilRate"/> gives for that fire's air now, paid for out of that air, which is never
    ///         cooled below the boiling point. The vapor joins the blanket's own tile, as <see cref="Boil"/>'s does.
    /// </summary>
    /// <remarks>
    ///     Each neighbour adds its own share, so a blanket with fire on several sides goes faster.
    /// </remarks>
    private void BoilFromRadiatedFire(Entity<KsFoamBlanketComponent> blanket, float seconds)
    {
        var component = blanket.Comp;
        if (component.RadiatingTiles.Count == 0)
            return;

        var energyPerUnit = GetEnergyPerUnit(component);
        if (component.LocalGridUid is { } gridUid && energyPerUnit > 0f)
        {
            foreach (var burningTile in component.RadiatingTiles.Keys)
            {
                if (component.Foam <= 0f)
                    break;

                if (_atmosphereSystem.GetTileMixture((gridUid, null, null), null, burningTile, excite: true) is not { Immutable: false } burningAir)
                    continue;

                var excess = burningAir.Temperature - component.BoilingPoint;
                var heatCapacity = _atmosphereSystem.GetHeatCapacity(burningAir, applyScaling: true);
                if (excess <= 0f || heatCapacity < Atmospherics.MinimumHeatCapacity)
                    continue;

                var boiled = MathF.Min(MathF.Min(GetAirBoilRate(component, burningAir.Temperature) * seconds, component.Foam),
                    excess * heatCapacity / energyPerUnit);
                if (boiled <= 0f)
                    continue;

                // The fire's air pays before the gas joins the blanket's tile, as in Boil.
                burningAir.Temperature -= boiled * energyPerUnit / heatCapacity;
                AddBoiledGas(blanket, gridUid, component.LocalTile, boiled);
            }
        }

        component.RadiatingTiles.Clear();
    }

    private bool AddBoiledGas(Entity<KsFoamBlanketComponent> blanket, EntityUid gridUid, Vector2i tile, float boiled)
    {
        var component = blanket.Comp;
        if (!_tileEffectAtmosSystem.TryAddBoiledGas(gridUid, tile, component.BoiledGas, boiled, boiled * component.SpecificHeat * component.BoilingPoint))
            return false;

        SetFoam(blanket, component.Foam - boiled);
        return true;
    }

    #region Fighting a chemfire

    /// <summary>
    ///     Foam has landed on a chemfire it does not put out at once: says so, at most every
    ///         <see cref="PopupInterval"/>. One that fights the foam starts losing to it on its next heat tick, through
    ///         <see cref="ContestFire"/>, and roars from there; one the foam cannot touch roars now.
    /// </summary>
    protected override void OnFoamReachedResistingFire(Entity<ChemicalFireComponent> fire)
    {
        var contestComponent = EnsureComp<KsFoamContestComponent>(fire.Owner);
        var curTime = _gameTiming.CurTime;

        if (!fire.Comp.FoamSmotherable)
            TryPlayResistSound(fire, contestComponent, curTime);

        if (curTime < contestComponent.NextPopupTime)
            return;

        contestComponent.NextPopupTime = curTime + PopupInterval;
        PopupAboutFire(fire, fire.Comp.FoamSmotherable ? "chemical-fire-foam-resisting" : "chemical-fire-foam-immune");
    }

    /// <summary>
    ///     Spends heat from a chemfire boiling away the foam blanket over it, if there is one: how a hot fire fights
    ///         the foam smothering it.
    /// </summary>
    /// <remarks>
    ///     Boiling a unit takes what <see cref="Boil"/> takes from a puddle, and its vapor joins the air the same way.
    /// </remarks>
    /// <param name="energy">The most heat, in joules, to spend.</param>
    /// <returns>The heat actually spent, in joules.</returns>
    public float HeatBlanket(EntityUid gridUid, Vector2i tile, float energy)
    {
        if (energy <= 0f || !TryGetBlanket(gridUid, tile, out var blanket))
            return 0f;

        var component = blanket.Comp;
        var energyPerUnit = GetEnergyPerUnit(component);
        if (energyPerUnit <= 0f)
            return 0f;

        var boiled = MathF.Min(component.Foam, energy / energyPerUnit);
        if (boiled <= 0f || !AddBoiledGas(blanket, gridUid, tile, boiled))
            return 0f;

        if (component.Foam <= 0f)
            StartDissolving(blanket);
        else
            Dirty(blanket);

        return boiled * energyPerUnit;
    }

    /// <summary>
    ///     A heat tick of a chemfire that fights foam: a blanket over it adds its foam over the fire's
    ///         <see cref="ChemicalFireComponent.FoamSmotherResistance"/> to the progress, and puts it out once that
    ///         reaches 1; without one, the fire recovers. The fire roars for as long as it is under foam.
    /// </summary>
    /// <remarks>
    ///     Called from <c>ChemicalFireSystem.OnHeatTile</c> rather than subscribed: only one system may take a
    ///         chemfire's heat tick, and the heat the fire spends on the foam comes out of the same budget.
    /// </remarks>
    /// <returns>Whether the foam put the fire out.</returns>
    public bool ContestFire(Entity<ChemicalFireComponent> fire, EntityUid gridUid, Vector2i tile, float seconds)
    {
        if (!fire.Comp.FoamSmotherable || fire.Comp.FoamSmotherResistance <= 0f)
            return false;

        if (TryGetBlanket(gridUid, tile, out var blanket))
        {
            var contestComponent = EnsureComp<KsFoamContestComponent>(fire.Owner);
            contestComponent.Progress += blanket.Comp.Foam * seconds / fire.Comp.FoamSmotherResistance;

            if (contestComponent.Progress >= 1f)
            {
                PopupAboutFire(fire, "chemical-fire-foam-smothered");
                _audioSystem.PlayPvs(blanket.Comp.SmotherSound, Transform(fire.Owner).Coordinates);
                _chemicalFireSystem.ExtinguishChemicalFire(fire);
                return true;
            }

            TryPlayResistSound(fire, contestComponent, _gameTiming.CurTime);
            _chemicalFireSystem.SetSmotherProgress(fire, contestComponent.Progress);
            return false;
        }

        if (!_contestQuery.TryComp(fire.Owner, out var recoveringComponent) || recoveringComponent.Progress <= 0f)
            return false;

        recoveringComponent.Progress = MathF.Max(recoveringComponent.Progress - fire.Comp.SmotherRecoveryRate * seconds, 0f);
        _chemicalFireSystem.SetSmotherProgress(fire, recoveringComponent.Progress);
        return false;
    }

    /// <summary>
    ///     Plays the fire's <see cref="ChemicalFireComponent.FoamResistSound"/>, at most every
    ///         <see cref="ResistSoundInterval"/>: an extinguisher lands foam several times a second.
    /// </summary>
    private void TryPlayResistSound(Entity<ChemicalFireComponent> fire, KsFoamContestComponent contestComponent, TimeSpan curTime)
    {
        if (curTime < contestComponent.NextResistSoundTime)
            return;

        contestComponent.NextResistSoundTime = curTime + ResistSoundInterval;
        _audioSystem.PlayPvs(fire.Comp.FoamResistSound, Transform(fire.Owner).Coordinates);
    }

    /// <summary>
    ///     A popup over the fire, naming it, for everyone who can see it.
    /// </summary>
    private void PopupAboutFire(Entity<ChemicalFireComponent> fire, string messageId)
    {
        if (Prototype(fire.Owner) is not { } prototype)
            return;

        _popupSystem.PopupCoordinates(
            KsPopupMessage.Create(messageId, ("fire", new KsPopupPrototypeName(prototype.ID))),
            Transform(fire.Owner).Coordinates,
            PopupType.SmallCaution);
    }

    #endregion
}
