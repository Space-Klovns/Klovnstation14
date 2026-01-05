// SPDX-FileCopyrightText: 2025 jhrushbe
//
// SPDX-License-Identifier: MPL-2.0

using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared.Atmos;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Random;
using Content.Shared.Administration.Logs;
using Robust.Server.GameObjects;
using Content.Server.Weapons.Ranged.Systems;
using Content.Server._FarHorizons.NodeContainer.Nodes;
using Robust.Shared.Utility;
using Content.Server.Destructible;
using Content.Shared.FixedPoint;
using Content.Shared.Destructible.Thresholds.Triggers;

namespace Content.Server._FarHorizons.Power.Generation.FissionGenerator;

public sealed class TurbineSystem : SharedTurbineSystem
{
    [Dependency] private readonly GunSystem _gun = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = null!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly NodeGroupSystem _nodeGroupSystem = default!;


    private readonly List<SoundSpecifier> _damageSoundList = [
        new SoundPathSpecifier("/Audio/_FarHorizons/Effects/engine_grump1.ogg"),
        new SoundPathSpecifier("/Audio/_FarHorizons/Effects/engine_grump2.ogg"),
        new SoundPathSpecifier("/Audio/_FarHorizons/Effects/engine_grump3.ogg"),
        new SoundPathSpecifier("/Audio/Effects/metal_slam5.ogg"),
        new SoundPathSpecifier("/Audio/Effects/metal_scrape2.ogg")
    ];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TurbineComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TurbineComponent, AtmosDeviceUpdateEvent>(OnUpdate);
    }

    private void OnStartup(Entity<TurbineComponent> entity, ref ComponentStartup args)
    {
        DestructibleComponent? destructibleComponent = null;
        if (!Resolve(entity, ref destructibleComponent))
            return;

        var damageNeeded = FixedPoint2.MaxValue;
        foreach (var threshold in destructibleComponent.Thresholds)
        {
            if (threshold.Trigger is not DamageTrigger trigger)
                continue;

            foreach (var behavior in threshold.Behaviors)
            {
                if (behavior is TurbineBladeDestructionBehaviour bladeDestructionBehaviour)
                    damageNeeded = Math.Min(damageNeeded.Float(), trigger.Damage.Float());
            }
        }

        entity.Comp.BladeBreakingPoint = damageNeeded;
        Dirty(entity);
    }

    private void OnUpdate(EntityUid uid, TurbineComponent comp, ref AtmosDeviceUpdateEvent args)
    {
        var supplier = Comp<PowerSupplierComponent>(uid);
        comp.SupplierMaxSupply = supplier.MaxSupply;

        supplier.MaxSupply = comp.LastGen;

        if (!_nodeContainer.TryGetNodes(uid, comp.InletName, comp.OutletName, out OffsetPipeNode? inlet, out OffsetPipeNode? outlet))
        {
            comp.HasPipes = false;
            return;
        }
        else
        {
            comp.HasPipes = true;
        }

        // Try to connect to a distant pipe
        if (inlet.ReachableNodes.Count == 0)
            _nodeGroupSystem.QueueReflood(inlet);
        if (outlet.ReachableNodes.Count == 0)
            _nodeGroupSystem.QueueReflood(outlet);

        UpdateAppearance(uid, comp);

        //var InletStartingPressure = inlet.Air.Pressure;
        //var TransferMoles = 0f;
        //if (InletStartingPressure > 0)
        //{
        //    TransferMoles = comp.FlowRate * InletStartingPressure / (Atmospherics.R * inlet.Air.Temperature);
        //}
        var AirContents = inlet.Air.RemoveVolume(Math.Min(comp.FlowRate * _atmosphereSystem.PumpSpeedup() * args.dt, inlet.Air.Volume));

        comp.LastVolumeTransfer = AirContents.Volume;
        comp.LastGen = 0;
        comp.Overtemp = AirContents?.Temperature >= comp.MaxTemp - 500;
        comp.Undertemp = AirContents?.Temperature <= comp.MinTemp;

        // Dump gas into atmosphere
        if (comp.Ruined || AirContents?.Temperature >= comp.MaxTemp)
        {
            var tile = _atmosphereSystem.GetTileMixture(uid, excite: true);

            if (tile != null)
            {
                _atmosphereSystem.Merge(tile, AirContents!);
            }

            if (!comp.Ruined && !_audio.IsPlaying(comp.AlarmAudioOvertemp))
            {
                comp.AlarmAudioOvertemp = _audio.PlayPvs(new SoundPathSpecifier("/Audio/_FarHorizons/Machines/alarm_buzzer.ogg"), uid, AudioParams.Default.WithLoop(true))?.Entity;
                _popupSystem.PopupEntity(Loc.GetString("turbine-overheat", ("owner", uid)), uid, PopupType.LargeCaution);
            }

            _atmosphereSystem.Merge(outlet.Air, AirContents!);

            // Prevent power from being gereated by residual gasses
            AirContents?.Clear();
        }
        else
        {
            comp.AlarmAudioOvertemp = _audio.Stop(comp.AlarmAudioOvertemp);
        }

        if (!comp.Ruined && AirContents != null)
        {
            var InputStartingEnergy = _atmosphereSystem.GetThermalEnergy(AirContents);
            var InputHeatCap = _atmosphereSystem.GetHeatCapacity(AirContents, true);

            // Prevents div by 0 if it would come up
            if (InputStartingEnergy <= 0)
            {
                InputStartingEnergy = 1;
            }
            if (InputHeatCap <= 0)
            {
                InputHeatCap = 1;
            }

            if (AirContents.Temperature > comp.MinTemp)
            {
                AirContents.Temperature = (float)Math.Max((InputStartingEnergy - ((InputStartingEnergy - (InputHeatCap * Atmospherics.T20C)) * 0.8)) / InputHeatCap, Atmospherics.T20C);
            }

            var OutputStartingEnergy = _atmosphereSystem.GetThermalEnergy(AirContents);
            var EnergyGenerated = comp.StatorLoad * (comp.RPM / 60);

            var DeltaE = InputStartingEnergy - OutputStartingEnergy;
            float NewRPM;

            if (DeltaE - EnergyGenerated > 0)
            {
                NewRPM = comp.RPM + (float)Math.Sqrt(2 * (Math.Max(DeltaE - EnergyGenerated, 0) / comp.TurbineMass));
            }
            else
            {
                NewRPM = comp.RPM - (float)Math.Sqrt(2 * (Math.Max(EnergyGenerated - DeltaE, 0) / comp.TurbineMass));
            }

            var NextGen = comp.StatorLoad * (Math.Max(NewRPM, 0) / 60);
            float NextRPM;

            if (DeltaE - NextGen > 0)
            {
                NextRPM = comp.RPM + (float)Math.Sqrt(2 * (Math.Max(DeltaE - NextGen, 0) / comp.TurbineMass));
            }
            else
            {
                NextRPM = comp.RPM - (float)Math.Sqrt(2 * (Math.Max(NextGen - DeltaE, 0) / comp.TurbineMass));
            }

            if (NewRPM < 0 || NextRPM < 0)
            {
                // Stator load is too high
                if (!_audio.IsPlaying(comp.AlarmAudioUnderspeed))
                {
                    comp.AlarmAudioUnderspeed = _audio.PlayPvs(new SoundPathSpecifier("/Audio/_FarHorizons/Machines/alarm_beep.ogg"), uid, AudioParams.Default.WithLoop(true).WithVolume(-4))?.Entity;
                }

                comp.Stalling = true;
                DirtyField(uid, comp, nameof(comp.Stalling));

                UpdateRpm(uid, comp, 0f);
            }
            else
            {
                comp.Stalling = false;
                DirtyField(uid, comp, nameof(comp.Stalling));

                UpdateRpm(uid, comp, NextRPM);
            }

            if (_audio.IsPlaying(comp.AlarmAudioUnderspeed) && (comp.RPM > 10 || (!comp.Stalling && comp.Undertemp))) { comp.AlarmAudioUnderspeed = _audio.Stop(comp.AlarmAudioUnderspeed); }

            if (comp.RPM > 10)
            {
                // Sacrifices must be made to have a smooth ramp up:
                // This will generate 2 audio streams every second with up to 4 of them playing at once... surely this can't go wrong :clueless:
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/_FarHorizons/Ambience/Objects/turbine_room.ogg"), uid, AudioParams.Default.WithPitchScale(comp.RPM / comp.BestRPM).WithVolume(-2));
            }

            // Calculate power generation
            var powerGenerated = comp.PowerMultiplier * comp.StatorLoad * (comp.RPM / 30) * (float)(1 / Math.Cosh(0.01 * (comp.RPM - comp.BestRPM)));
            if (float.IsNaN(powerGenerated))
            {
                DebugTools.Assert(true, $"Turbine {ToPrettyString(uid)} made NaN power!");
                Log.Error($"Turbine {ToPrettyString(uid)} made NaN power!");
                TearApart((uid, comp));

                return;
            }

            comp.LastGen = powerGenerated;

            // Damage the turbines during overspeed, linear increase from 18% to 45% then stays at 45%
            if (comp.Overspeed && _random.NextFloat() < 0.15 * Math.Min(comp.RPM / comp.BestRPM, 3))
            {
                DamageableSystem.TryChangeDamage(uid, comp.BladeOverspeedDamage, ignoreResistances: true);
                _audio.PlayPvs(_damageSoundList[_random.Next(0, _damageSoundList.Count - 1)], uid, AudioParams.Default.WithVariation(0.25f).WithVolume(-1));
            }

            _atmosphereSystem.Merge(outlet.Air, AirContents);
        }
        //inlet.Air.Volume = comp.FlowRate;
        AirContents!.Volume = comp.FlowRate;

        // Explode
        if (!comp.Ruined && comp.RPM > comp.BestRPM * 4)
            TearApart((uid, comp));
    }

    public void TearApart(Entity<TurbineComponent?> entity, EntityUid? cause = null)
    {
        if (!Resolve(entity.Owner, ref entity.Comp))
            return;

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/metal_break5.ogg"), entity, AudioParams.Default);
        _popupSystem.PopupEntity(Loc.GetString("turbine-explode", ("owner", entity.Owner)), entity, PopupType.LargeCaution);
        _explosion.TriggerExplosive(entity, explosive: null, delete: false, totalIntensity: entity.Comp.RPM / 10, 5);
        ShootShrapnel(entity);

        _adminLogger.Add(LogType.Explosion, LogImpact.High, $"{ToPrettyString(entity.Owner)}'s turbine blade was destroyed by {(cause == null ? "mechanical causes (possibly overspeeding for too long?)" : ToPrettyString(cause))}");
        entity.Comp.Ruined = true;
        DirtyField(entity, nameof(entity.Comp.Ruined));

        UpdateRpm(entity, entity!, 0f);
        UpdateAppearance(entity, entity);
    }

    private void ShootShrapnel(EntityUid uid)
    {
        var ShrapnelCount = _random.Next(5, 20);
        for (var i = 0; i < ShrapnelCount; i++)
        {
            _gun.ShootProjectile(Spawn("TurbineBladeShrapnel", _transformSystem.GetMapCoordinates(uid)), _random.NextAngle().ToVec().Normalized(), _random.NextVector2(2, 6), uid, uid);
        }
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<TurbineComponent>();

        while (query.MoveNext(out var uid, out var turbine))
        {
            UpdateUI(uid, turbine);
        }
    }

    private void UpdateUI(EntityUid uid, TurbineComponent turbine)
    {
        if (!_uiSystem.IsUiOpen(uid, TurbineUiKey.Key))
            return;

        _uiSystem.SetUiState(uid, TurbineUiKey.Key,
           new TurbineBuiState
           {
               Overspeed = turbine.Overspeed,
               Stalling = turbine.Stalling,
               Overtemp = turbine.Overtemp,
               Undertemp = turbine.Undertemp,

               RPM = turbine.RPM,
               BestRPM = turbine.BestRPM,

               FlowRateMin = 0,
               FlowRateMax = turbine.FlowRateMax,
               FlowRate = turbine.FlowRate,

               StatorLoadMin = 1000,
               StatorLoadMax = 500000,
               StatorLoad = turbine.StatorLoad,
           });
    }
}
