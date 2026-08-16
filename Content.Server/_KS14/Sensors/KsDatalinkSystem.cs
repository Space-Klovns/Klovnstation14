using Content.Server.Power.Components;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Sensors;
using Content.Shared.Examine;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Machine plumbing for datalink transmitters and receivers: BUI wiring,
///         APC load scaling and periodic UI refresh. The broadcast/ingest logic
///         (who hears whom) lives in <see cref="KsSensorSystem"/>.
/// </summary>
public sealed partial class KsDatalinkSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private KsSensorSystem _sensor = default!;

    /// <summary>
    ///     UI refresh cadence for open datalink windows, sharing the sensor tick
    ///         (<see cref="KsCCVars.SensorsUpdateInterval"/>) so the two never drift.
    /// </summary>
    private TimeSpan _updateInterval;

    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();

        _cfg.OnValueChanged(KsCCVars.SensorsUpdateInterval, v => _updateInterval = TimeSpan.FromSeconds(v), invokeImmediately: true);

        Subs.BuiEvents<KsDatalinkTransmitterComponent>(KsDatalinkTransmitterUiKey.Key, subs =>
        {
            subs.Event<KsDatalinkToggleMessage>(OnTransmitterToggle);
            subs.Event<KsDatalinkSetFrequencyMessage>(OnTransmitterSetFrequency);
            subs.Event<KsDatalinkSetPowerMessage>(OnTransmitterSetPower);
        });

        Subs.BuiEvents<KsDatalinkReceiverComponent>(KsDatalinkReceiverUiKey.Key, subs =>
        {
            subs.Event<KsDatalinkToggleMessage>(OnReceiverToggle);
            subs.Event<KsDatalinkSetFrequencyMessage>(OnReceiverSetFrequency);
        });

        SubscribeLocalEvent<KsDatalinkTransmitterComponent, MapInitEvent>(OnTransmitterMapInit);

        SubscribeLocalEvent<KsDatalinkTransmitterComponent, PowerChangedEvent>(OnTransmitterPowerChanged);
        SubscribeLocalEvent<KsDatalinkReceiverComponent, PowerChangedEvent>(OnReceiverPowerChanged);

        SubscribeLocalEvent<KsDatalinkTransmitterComponent, ExaminedEvent>(OnTransmitterExamine);
        SubscribeLocalEvent<KsDatalinkReceiverComponent, ExaminedEvent>(OnReceiverExamine);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        if (curTime < _nextTick)
            return;

        // Hitch-safe: never tries to catch up missed ticks.
        _nextTick = curTime + _updateInterval;

        var txQuery = EntityQueryEnumerator<KsDatalinkTransmitterComponent>();
        while (txQuery.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, KsDatalinkTransmitterUiKey.Key))
                UpdateTransmitterUi((uid, comp));
        }

        var rxQuery = EntityQueryEnumerator<KsDatalinkReceiverComponent>();
        while (rxQuery.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, KsDatalinkReceiverUiKey.Key))
                UpdateReceiverUi((uid, comp));
        }
    }

    #region Transmitter

    private void OnTransmitterMapInit(Entity<KsDatalinkTransmitterComponent> entity, ref MapInitEvent args)
    {
        // Sync the APC load with the component's actual settings from the start, or a
        // freshly built transmitter broadcasts at full range for its idle YAML load
        // until someone touches the UI.
        UpdateTransmitterPowerLoad(entity);
    }

    private void OnTransmitterToggle(Entity<KsDatalinkTransmitterComponent> entity, ref KsDatalinkToggleMessage args)
    {
        entity.Comp.Enabled = !entity.Comp.Enabled;

        UpdateTransmitterPowerLoad(entity);
        UpdateTransmitterUi(entity);
    }

    private void OnTransmitterSetFrequency(Entity<KsDatalinkTransmitterComponent> entity, ref KsDatalinkSetFrequencyMessage args)
    {
        entity.Comp.Frequency = Math.Clamp(args.Frequency, KsDatalink.MinFrequency, KsDatalink.MaxFrequency);

        UpdateTransmitterUi(entity);
    }

    private void OnTransmitterSetPower(Entity<KsDatalinkTransmitterComponent> entity, ref KsDatalinkSetPowerMessage args)
    {
        entity.Comp.PowerFraction = Math.Clamp(args.PowerFraction, 0f, 1f);

        UpdateTransmitterPowerLoad(entity);
        UpdateTransmitterUi(entity);
    }

    private void OnTransmitterPowerChanged(Entity<KsDatalinkTransmitterComponent> entity, ref PowerChangedEvent args)
    {
        UpdateTransmitterUi(entity);
    }

    private void OnTransmitterExamine(EntityUid uid, KsDatalinkTransmitterComponent comp, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("ks-datalink-examine-frequency", ("frequency", comp.Frequency)));
    }

    /// <summary>A switched-off transmitter only draws its idle load.</summary>
    private void UpdateTransmitterPowerLoad(Entity<KsDatalinkTransmitterComponent> entity)
    {
        if (!TryComp<ApcPowerReceiverComponent>(entity, out var receiver))
            return;

        receiver.Load = entity.Comp.Enabled
            ? entity.Comp.BasePowerDraw + entity.Comp.PowerFraction * (entity.Comp.MaxPowerDraw - entity.Comp.BasePowerDraw)
            : entity.Comp.BasePowerDraw;
    }

    private void UpdateTransmitterUi(Entity<KsDatalinkTransmitterComponent> entity)
    {
        var state = new KsDatalinkTransmitterBuiState(
            entity.Comp.Enabled,
            _power.IsPowered(entity.Owner),
            entity.Comp.Frequency,
            entity.Comp.PowerFraction,
            entity.Comp.MaxRange,
            entity.Comp.BroadcastAllFrequencies,
            entity.Comp.UnlimitedRange);

        _ui.SetUiState(entity.Owner, KsDatalinkTransmitterUiKey.Key, state);
    }

    #endregion

    #region Receiver

    private void OnReceiverToggle(Entity<KsDatalinkReceiverComponent> entity, ref KsDatalinkToggleMessage args)
    {
        entity.Comp.Enabled = !entity.Comp.Enabled;

        UpdateReceiverUi(entity);
    }

    private void OnReceiverSetFrequency(Entity<KsDatalinkReceiverComponent> entity, ref KsDatalinkSetFrequencyMessage args)
    {
        entity.Comp.Frequency = Math.Clamp(args.Frequency, KsDatalink.MinFrequency, KsDatalink.MaxFrequency);

        UpdateReceiverUi(entity);
    }

    private void OnReceiverPowerChanged(Entity<KsDatalinkReceiverComponent> entity, ref PowerChangedEvent args)
    {
        UpdateReceiverUi(entity);
    }

    private void OnReceiverExamine(EntityUid uid, KsDatalinkReceiverComponent comp, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("ks-datalink-examine-frequency", ("frequency", comp.Frequency)));
    }

    private void UpdateReceiverUi(Entity<KsDatalinkReceiverComponent> entity)
    {
        var state = new KsDatalinkReceiverBuiState(
            entity.Comp.Enabled,
            _power.IsPowered(entity.Owner),
            entity.Comp.Frequency,
            _sensor.GetHeardCount(entity.Owner));

        _ui.SetUiState(entity.Owner, KsDatalinkReceiverUiKey.Key, state);
    }

    #endregion
}
