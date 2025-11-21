using Content.Shared._KS14.TeslaGate;
using Content.Shared.Damage;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System.Runtime.CompilerServices;
using Content.Server.AlertLevel;
using Content.Shared.Power;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Server._KS14.TeslaGate;

public sealed class TeslaGateSystem : SharedTeslaGateSystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _powerReceiverSystem = default!;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanStartWork(EntityUid uid)
    {
        if (!_powerReceiverSystem.IsPowered(uid))
            return false;

        return true;
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

        _audioSystem.PlayPvs(teslaGateComponent.ShockSound, uid);

        UpdateAppearance(teslaGate, true, TeslaGateVisualState.Active);
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

        UpdateAppearance(teslaGate, false, TeslaGateVisualState.Inactive);
        Dirty(teslaGate);

        _audioSystem.PlayPvs(teslaGateComponent.StartingSound, uid);
    }

    private void ResetAccumulator(TeslaGateComponent teslaGateComponent)
    {
        teslaGateComponent.NextPulse = _gameTiming.CurTime + teslaGateComponent.PulseInterval;
        teslaGateComponent.LastShockTime = TimeSpan.MinValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Zap(EntityUid uid, DamageSpecifier damage)
    {
        _damageableSystem.TryChangeDamage(uid, damage, ignoreResistances: true);
    }

    public void Enable(Entity<TeslaGateComponent> teslaGate)
    {
        var (uid, teslaGateComponent) = teslaGate;

        if (CanStartWork(uid))
            _audioSystem.PlayPvs(teslaGateComponent.StartingSound, uid);

        ResetAccumulator(teslaGateComponent);
        teslaGateComponent.Enabled = true;
    }

    public void Disable(Entity<TeslaGateComponent> teslaGate)
    {
        teslaGate.Comp.Enabled = false;
        ResetAccumulator(teslaGate);
    }

    public override void OnPowerChange(Entity<TeslaGateComponent> teslaGate, ref PowerChangedEvent args)
    {
        // dont care if its already enabled
        if (args.Powered)
            Enable(teslaGate);
        else
            Disable(teslaGate);
    }

    private void CollideAct(TeslaGateComponent teslaGateComponent, EntityUid otherEntity)
    {
        if (!teslaGateComponent.ThingsBeingShocked.Add(GetNetEntity(otherEntity)))
            return;

        Zap(otherEntity, teslaGateComponent.ShockDamage);
    }

    private void OnGateStartCollide(Entity<TeslaGateComponent> teslaGate, ref StartCollideEvent args)
    {
        var (uid, teslaGateComponent) = teslaGate;

        if (teslaGateComponent.CurrentlyShocking)
            CollideAct(teslaGateComponent, args.OtherEntity);
    }

    private void OnAlertLevelChanged(AlertLevelChangedEvent alertEvent)
    {
        if (!TryComp<AlertLevelComponent>(alertEvent.Station, out var alertLevelComponent))
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
