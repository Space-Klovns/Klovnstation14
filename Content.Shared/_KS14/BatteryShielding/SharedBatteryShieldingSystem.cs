using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.Sparks;
using Content.Shared.Administration.Logs;
using Content.Shared.Atmos.Components;
using Content.Shared.Database;
using Content.Shared.Destructible;
using Content.Shared.Emag.Systems;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.BatteryShielding;

public abstract partial class SharedBatteryShieldingSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private ISharedAdminLogManager _adminLogManager = default!;
    [Dependency] private SharedBatterySystem _batterySystem = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedSparksSystem _sparksSystem = default!;
    [Dependency] private SharedDestructibleSystem _destructibleSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private EmagSystem _emagSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BatteryShieldingComponent, GotEmaggedEvent>(OnGotEmagged);
        SubscribeLocalEvent<BatteryShieldingComponent, BatteryShieldingToggleMessage>(OnToggleMessage);
        SubscribeLocalEvent<BatteryShieldingComponent, KsGasMaxPressureAttemptLoseIntegrityEvent>(OnGasMaxPressureAttemptLoseIntegrity);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_gameTiming.IsFirstTimePredicted)
            return;

        var eqe = EntityQueryEnumerator<BtfoingBatteryShieldingComponent, BatteryShieldingComponent>();
        while (eqe.MoveNext(out var uid, out var btfoingComponent, out var shieldingComponent))
        {
            if (_gameTiming.CurTime < btfoingComponent.BtfoTime)
                continue;

            _audioSystem.PlayPredicted(shieldingComponent.EmagImplosionSound, Transform(uid).Coordinates, user: btfoingComponent.UserUid);
            _destructibleSystem.DestroyEntity(uid);
            PredictedQueueDel(uid);
        }
    }

    private void OnGotEmagged(Entity<BatteryShieldingComponent> entity, ref GotEmaggedEvent args)
    {
        if (!_emagSystem.CompareFlag(args.Type, EmagType.Interaction) ||
            _emagSystem.CheckFlag(entity.Owner, EmagType.Interaction))
            return;

        args.Handled = true;
        args.Repeatable = false;

        if (!entity.Comp.Enabled)
            return;

        Disable(entity);

        var coordinates = Transform(entity).Coordinates;
        _sparksSystem.DoSpark(coordinates, SharedSparksSystem.DefaultSparkPrototype, soundSpecifier: SharedSparksSystem.DefaultSoundSpecifier, user: args.UserUid);
        _sparksSystem.ExposeSpark(coordinates, exposedTemperature: 2500f, exposedVolume: 10f);
    }

    private void OnToggleMessage(Entity<BatteryShieldingComponent> entity, ref BatteryShieldingToggleMessage args)
    {
        if (entity.Comp.Enabled)
            Disable(entity);
        else
        {
            if (_batterySystem.GetCharge(entity.Owner) <
                entity.Comp.DischargeRate)
                return;

            Enable(entity, userUid: args.Actor);
        }
    }

    private void OnGasMaxPressureAttemptLoseIntegrity(Entity<BatteryShieldingComponent> entity, ref KsGasMaxPressureAttemptLoseIntegrityEvent args)
    {
        if (args.Cancelled ||
            !entity.Comp.Enabled)
            return;

        var useRate = GetActiveEnergyUseRate(entity, args.Component);
        entity.Comp.DischargeRate = useRate;
        Dirty(entity);

        UpdateUi(entity);

        if (!_batterySystem.TryUseCharge(entity.Owner, useRate * args.DeltaTime))
        {
            Disable(entity, adminReason: "No more power is available");

            if (entity.Comp.FailPopupLoc is { } failPopupLocId)
                _popupSystem.PopupEntity(Loc.GetString(failPopupLocId), entity, type: PopupType.LargeCaution);

            var coordinates = Transform(entity).Coordinates;
            _sparksSystem.DoSpark(coordinates, SharedSparksSystem.DefaultSparkPrototype, soundSpecifier: SharedSparksSystem.DefaultSoundSpecifier);
            _sparksSystem.ExposeSpark(coordinates, exposedTemperature: 2500f, exposedVolume: 10f);
            return;
        }

        if (entity.Comp.FalterPopupLoc is { } falterPopupLocId)
            _popupSystem.PopupEntity(Loc.GetString(falterPopupLocId), entity, type: PopupType.SmallCaution);

        args.Cancelled = true;
    }

    /// <returns>Amount of charge used in one second.</returns>
    private static float GetActiveEnergyUseRate(BatteryShieldingComponent shieldingComponent, IGasMaxPressureHolder gasComponent)
        => shieldingComponent.ChargeUseRateMultiplier * MathF.Log10(MathF.Max(1f, gasComponent.Air.Pressure - gasComponent.Overpressure));

    public void Disable(Entity<BatteryShieldingComponent> entity, string? adminReason = null)
    {
        entity.Comp.Enabled = false;
        Dirty(entity);

        _appearanceSystem.SetData(entity, BatteryShieldingVisuals.Active, false);
        UpdateUi(entity);

        if (entity.Comp.RaiseAdminLogs)
            _adminLogManager.Add(LogType.AtmosPowerChanged, LogImpact.Medium, $"Battery-shielded entity {ToPrettyString(entity)} is disabled {(adminReason == null ? "for unknown reason" : "for reason: " + adminReason)}");
    }

    public void Enable(Entity<BatteryShieldingComponent> entity, EntityUid? userUid = null)
    {
        if (_emagSystem.CheckFlag(entity.Owner, EmagType.Interaction))
        {
            if (HasComp<BtfoingBatteryShieldingComponent>(entity))
                return;

            if (entity.Comp.EmagMalfunctionPopupLoc is { } malfPopupLoc)
                _popupSystem.PopupPredicted(Loc.GetString(malfPopupLoc), entity.Owner, recipient: userUid, type: PopupType.LargeCaution);

            _audioSystem.PlayPredicted(entity.Comp.EmagMalfunctionSound, entity.Owner, user: userUid);

            var btfoingComponent = AddComp<BtfoingBatteryShieldingComponent>(entity);
            btfoingComponent.BtfoTime = _gameTiming.CurTime + entity.Comp.EmagMalfunctionDuration;
            btfoingComponent.UserUid = userUid;

            return;
        }

        entity.Comp.Enabled = true;
        Dirty(entity);

        _appearanceSystem.SetData(entity, BatteryShieldingVisuals.Active, true);
        UpdateUi(entity);
    }

    protected virtual void UpdateUi(Entity<BatteryShieldingComponent> entity) { }
}
