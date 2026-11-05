using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.NodeGroups;
using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Atmos;

/// <summary>
///     What a compressing device does with its heat, for every device that compresses: recovers most of what it can't keep
///         in the gas as electricity, is billed for the rest, and keeps a tally for its window.
/// </summary>
/// <remarks>
///     <para>Energy is the point. The work a device does goes into the gas and the room, or comes back as electricity, so
///         the load it pays is what actually stayed in the atmosphere. Past <see cref="KsCCVars.AtmosPumpMaxLoadMultiplier"/>
///         the rest goes unpaid, rather than slowing the pump down.</para>
///
///     <para>A device's load can't simply be set. Every receiver on an APC counts as unpowered the moment the APC can't
///         meet them all, and an APC's supply lags its demand: it follows at <c>supplyRampRate</c>, and covers a gap of up
///         to <c>supplyRampTolerance</c>. So a load climbs a little every tick, and only while the APC's supply is well
///         inside that gap and it can go on supplying more (<see cref="ApcHeadroom"/>), however many other devices are
///         climbing beside it. Falling needs none of this, since a shortfall can't come of it.</para>
/// </remarks>
public sealed partial class KsPumpHeatSystem : KsSharedPumpHeatSystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<ApcPowerReceiverComponent> _receiverQuery = default!;
    [Dependency] private EntityQuery<KsPumpHeatComponent> _pumpHeatQuery = default!;
    [Dependency] private EntityQuery<PowerNetworkBatteryComponent> _batteryQuery = default!;

    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How long a load out of power stays at idle, so a device can't hunt on a dying APC.
    /// </summary>
    private static readonly TimeSpan PowerLossHold = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Seconds over which the readings and the target load follow the work, so that a pump's load doesn't chase every
    ///         update.
    /// </summary>
    private const float SmoothingSeconds = 5f;

    /// <summary>
    ///     Seconds a device takes to climb from idle to its maximum load, if its APC keeps up.
    /// </summary>
    private const float LoadClimbSeconds = 16f;

    /// <summary>
    ///     Share of an APC's charge rate past which no device on it raises its load: above that rate its battery drains.
    /// </summary>
    private const float ApcChargeRateMargin = 0.8f;

    /// <summary>
    ///     Share of an APC's ramp tolerance its supply may run ahead of its ramp before devices stop climbing. Past the whole
    ///         tolerance, everything on the APC browns out. The rest is room for machines to switch on meanwhile.
    /// </summary>
    private const float ApcRampMargin = 0.5f;

    /// <summary>
    ///     Share of an APC's battery below which its devices give up their raised load.
    /// </summary>
    private const float ApcMinimumCharge = 0.25f;

    /// <summary>
    ///     Readings below this, in watts, count as nothing, and let the component go.
    /// </summary>
    private const float MinimumPower = 1f;

    /// <summary>
    ///     Samples with no work in them before the component goes, so that a device doing a trickle of work doesn't have it
    ///         added and removed, and networked, every second.
    /// </summary>
    private const int QuietSamplesToRemove = 10;

    private float _heatRecovery;
    private float _maxLoadMultiplier;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.AtmosPumpHeatRecovery, value => _heatRecovery = Math.Clamp(value, 0f, 1f), invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.AtmosPumpMaxLoadMultiplier, value => _maxLoadMultiplier = Math.Max(value, 0f), invokeImmediately: true);
    }

    /// <summary>
    ///     How much of <paramref name="heat"/>, in unscaled joules, a device turns back into electricity. It needs to be on
    ///         the grid to do that, so anything off it recovers nothing.
    /// </summary>
    public float RecoverableHeat(EntityUid deviceUid, float heat)
    {
        return _receiverQuery.TryComp(deviceUid, out var receiverComponent) && receiverComponent is { NeedsPower: true, Powered: true }
            ? heat * _heatRecovery
            : 0f;
    }

    /// <summary>
    ///     Puts one transfer's heat on a device's tally, in unscaled joules.
    /// </summary>
    public void Record(EntityUid deviceUid, float gasHeat, float roomHeat, float recoveredHeat)
    {
        if (!_pumpHeatQuery.TryComp(deviceUid, out var pumpHeatComponent))
        {
            pumpHeatComponent = AddComp<KsPumpHeatComponent>(deviceUid);
            pumpHeatComponent.IdleLoad = _receiverQuery.CompOrNull(deviceUid)?.Load ?? 0f;

            var curTime = _gameTiming.CurTime;
            pumpHeatComponent.LastSample = curTime;
            pumpHeatComponent.NextSample = curTime + SampleInterval;
        }

        pumpHeatComponent.PendingGasHeat += gasHeat;
        pumpHeatComponent.PendingRoomHeat += roomHeat;
        pumpHeatComponent.PendingRecoveredHeat += recoveredHeat;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsPumpHeatComponent>();
        while (query.MoveNext(out var uid, out var pumpHeatComponent))
        {
            if (pumpHeatComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            Entity<KsPumpHeatComponent> entity = (uid, pumpHeatComponent);
            if (curTime >= pumpHeatComponent.NextSample)
                Sample(entity, curTime);

            _receiverQuery.TryComp(uid, out var receiverComponent);
            if (receiverComponent != null)
                StepLoad((uid, pumpHeatComponent, receiverComponent), curTime, frameTime);

            var atIdle = receiverComponent == null || MathF.Abs(receiverComponent.Load - pumpHeatComponent.IdleLoad) < 0.01f;
            if (atIdle &&
                pumpHeatComponent.QuietSamples >= QuietSamplesToRemove &&
                pumpHeatComponent.GasPower + pumpHeatComponent.RoomPower + pumpHeatComponent.RecoveredPower < MinimumPower)
                RemCompDeferred(uid, pumpHeatComponent);
        }
    }

    /// <summary>
    ///     Devices update on atmos's schedule, which can run over a tick when it's busy, so the tally is divided by the time
    ///         it really covers.
    /// </summary>
    private void Sample(Entity<KsPumpHeatComponent> entity, TimeSpan curTime)
    {
        var component = entity.Comp;
        var elapsed = (float)(curTime - component.LastSample).TotalSeconds;
        component.LastSample = curTime;
        component.NextSample = curTime + SampleInterval;
        if (elapsed <= 0f)
            return;

        var worked = component.PendingGasHeat + component.PendingRoomHeat + component.PendingRecoveredHeat > 0f;
        component.QuietSamples = worked ? 0 : component.QuietSamples + 1;

        var blend = 1f - MathF.Exp(-elapsed / SmoothingSeconds);
        var scale = 1f / (elapsed * _atmosphereSystem.HeatScale);
        var (gasBefore, roomBefore, recoveredBefore) = (component.GasPower, component.RoomPower, component.RecoveredPower);

        component.GasPower += (component.PendingGasHeat * scale - component.GasPower) * blend;
        component.RoomPower += (component.PendingRoomHeat * scale - component.RoomPower) * blend;
        component.RecoveredPower += (component.PendingRecoveredHeat * scale - component.RecoveredPower) * blend;

        component.PendingGasHeat = 0f;
        component.PendingRoomHeat = 0f;
        component.PendingRecoveredHeat = 0f;

        // Every device holding the component samples each second, and a footnote is rarely open on any of them.
        if (MathF.Abs(component.GasPower - gasBefore) +
            MathF.Abs(component.RoomPower - roomBefore) +
            MathF.Abs(component.RecoveredPower - recoveredBefore) >= MinimumPower)
            Dirty(entity);
    }

    private void StepLoad(Entity<KsPumpHeatComponent, ApcPowerReceiverComponent> entity, TimeSpan curTime, float frameTime)
    {
        var (_, component, receiverComponent) = entity;
        var idleLoad = component.IdleLoad;

        if (receiverComponent is { NeedsPower: true, Powered: false })
        {
            receiverComponent.Load = idleLoad;
            component.HoldIdleUntil = curTime + PowerLossHold;
            return;
        }

        var workRange = idleLoad * _maxLoadMultiplier;
        var target = idleLoad;
        if (receiverComponent.NeedsPower && curTime >= component.HoldIdleUntil)
            target += Math.Clamp(component.GasPower + component.RoomPower, 0f, workRange);

        // Near its limit an APC takes no more, and past it, or running out, it gets some back.
        if (target > idleLoad)
        {
            var (canClimb, canHold) = ApcHeadroom(receiverComponent);
            if (!canHold)
                target = idleLoad;
            else if (!canClimb)
                target = MathF.Min(target, receiverComponent.Load);
        }

        var step = (workRange > 0f ? workRange : idleLoad) / LoadClimbSeconds * frameTime;
        receiverComponent.Load = Math.Clamp(target, receiverComponent.Load - step, receiverComponent.Load + step);
    }

    /// <returns>
    ///     Whether the APC feeding <paramref name="receiverComponent"/> has room for more load, and whether it can go on carrying what
    ///         it does: past its charge rate its battery drains, whatever it can supply for now.
    /// </returns>
    private (bool CanClimb, bool CanHold) ApcHeadroom(ApcPowerReceiverComponent receiverComponent)
    {
        if (receiverComponent.Provider?.Net is not ApcNet net)
            return (false, false);

        var canClimb = true;
        foreach (var apc in net.Apcs)
        {
            if (!_batteryQuery.TryComp(apc.Owner, out var batteryComponent))
                continue;

            var battery = batteryComponent.NetworkBattery;
            if (battery.CurrentSupply > battery.MaxChargeRate || battery.CurrentStorage < battery.Capacity * ApcMinimumCharge)
                return (false, false);

            // What comes straight through from its input is outside the ramp.
            var rampGap = battery.CurrentSupply - battery.CurrentReceiving * battery.Efficiency - battery.SupplyRampPosition;
            canClimb &= battery.CurrentSupply < battery.MaxChargeRate * ApcChargeRateMargin &&
                        rampGap < battery.SupplyRampTolerance * ApcRampMargin;
        }

        return (canClimb, true);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsPumpHeatComponent> entity, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(entity.Owner) && _receiverQuery.TryComp(entity.Owner, out var receiverComponent))
            receiverComponent.Load = entity.Comp.IdleLoad;
    }
}
