// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.TeslaGate;
using Content.Shared.Damage;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using System.Runtime.CompilerServices;
using Content.Server.AlertLevel;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Server._KS14.TeslaGate;

public sealed class TeslaGateSystem : SharedTeslaGateSystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TeslaGateComponent, StartCollideEvent>(OnGateStartCollide);
        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<TeslaGateComponent>();
        while (query.MoveNext(out var uid, out var teslaGateComponent))
        {
            var teslaGate = (uid, teslaGateComponent);
            var canShock = CanWork(uid, teslaGateComponent, out var canStart);

            if (teslaGateComponent.IsTimerWireCut)
                continue;

            if (teslaGateComponent.CurrentlyShocking)
            {
                if (!canShock || IsFinishedShocking(teslaGateComponent))
                    QuitZappinEmAll(teslaGate);

                continue;
            }

            if (!canStart)
                continue;

            if (_gameTiming.CurTime < teslaGateComponent.NextPulse)
                continue;

            if (canShock)
                ZapEmAll(teslaGate);
        }
    }

    // HELP
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanWork(EntityUid uid, TeslaGateComponent teslaGateComponent, out bool canStart)
    {
        canStart = CanStartWork(uid);
        if (!teslaGateComponent.Enabled)
            return false;

        if (!canStart)
            return false;

        return true;
    }

    private void ZapEmAll(Entity<TeslaGateComponent> teslaGate)
    {
        var (uid, teslaGateComponent) = teslaGate;
        teslaGateComponent.LastShockTime = _gameTiming.CurTime;
        teslaGateComponent.NextPulse = _gameTiming.CurTime + teslaGate.Comp.PulseInterval;

        AudioSystem.PlayPvs(teslaGateComponent.ShockSound, uid);

        UpdateAppearance(teslaGate, true);
        Dirty(teslaGate);

        teslaGateComponent.CurrentlyShocking = true;
        foreach (var entity in _physicsSystem.GetContactingEntities(uid))
            CollideAct(teslaGateComponent, entity);
    }

    private void QuitZappinEmAll(Entity<TeslaGateComponent> teslaGate)
    {
        var (uid, teslaGateComponent) = teslaGate;

        teslaGateComponent.CurrentlyShocking = false;
        teslaGateComponent.ThingsBeingShocked.Clear();

        UpdateAppearance(teslaGate, false);
        Dirty(teslaGate);

        AudioSystem.PlayPvs(teslaGateComponent.StartingSound, uid);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Zap(EntityUid uid, DamageSpecifier damage)
    {
        _damageableSystem.TryChangeDamage(uid, damage, ignoreResistances: true);
    }

    private void CollideAct(TeslaGateComponent teslaGateComponent, EntityUid otherEntity)
    {
        if (!teslaGateComponent.ThingsBeingShocked.Add(GetNetEntity(otherEntity)))
            return;

        Zap(otherEntity, teslaGateComponent.ShockDamage);
    }

    private void OnGateStartCollide(Entity<TeslaGateComponent> teslaGate, ref StartCollideEvent args)
    {
        if (teslaGate.Comp.CurrentlyShocking)
            CollideAct(teslaGate, args.OtherEntity);
    }

    private void OnAlertLevelChanged(AlertLevelChangedEvent alertEvent)
    {
        if (!TryComp<AlertLevelComponent>(alertEvent.Station, out var _))
            return;

        var alertLevel = alertEvent.AlertLevel;

        var query = EntityQueryEnumerator<TeslaGateComponent>();
        while (query.MoveNext(out var uid, out var teslaGateComponent))
        {
            var teslaGate = (uid, teslaGateComponent);

            if (teslaGateComponent.IsForceHacked)
                continue;

            if (teslaGateComponent.EnabledAlertLevels.Contains(alertLevel))
                Enable(teslaGate);
            else
                Disable(teslaGate);
        }
    }
}
