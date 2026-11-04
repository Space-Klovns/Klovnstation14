using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Administration.Logs;
using Content.Shared.Atmos.Piping.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;

namespace Content.Shared._KS14.Atmos.TemperatureGate;

/// <summary>
///     Everything a player does to a <see cref="KsTemperatureGateComponent"/>, which runs on both sides so that it's
///         predicted. The gate's flow is server-only, in its subclass.
/// </summary>
public abstract partial class KsSharedTemperatureGateSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLogManager = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedPowerReceiverSystem _powerReceiverSystem = default!;
    [Dependency] private SharedToolSystem _toolSystem = default!;
    [Dependency] protected SharedUserInterfaceSystem UserInterfaceSystem = default!;

    [SubscribeLocalEvent]
    private void OnInit(Entity<KsTemperatureGateComponent> entity, ref ComponentInit args)
    {
        UpdateAppearance(entity);
    }

    [SubscribeLocalEvent]
    private void OnPowerChanged(Entity<KsTemperatureGateComponent> entity, ref PowerChangedEvent args)
    {
        UpdateAppearance(entity);
    }

    [SubscribeLocalEvent]
    private void OnToggleStatusMessage(Entity<KsTemperatureGateComponent> entity, ref KsTemperatureGateToggleStatusMessage args)
    {
        SetEnabled(entity, args.Enabled, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnChangeTargetMessage(Entity<KsTemperatureGateComponent> entity, ref KsTemperatureGateChangeTargetMessage args)
    {
        SetTarget(entity, args.Temperature, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnChangeModeMessage(Entity<KsTemperatureGateComponent> entity, ref KsTemperatureGateChangeModeMessage args)
    {
        SetMode(entity, args.Mode, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnChangeToleranceMessage(Entity<KsTemperatureGateComponent> entity, ref KsTemperatureGateChangeToleranceMessage args)
    {
        SetTolerance(entity, args.Tolerance, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnChangeRangeMessage(Entity<KsTemperatureGateComponent> entity, ref KsTemperatureGateChangeRangeMessage args)
    {
        SetRange(entity, args.Minimum, args.Maximum, args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnLeaveAtmosphere(Entity<KsTemperatureGateComponent> entity, ref AtmosDeviceDisabledEvent args)
    {
        entity.Comp.Enabled = false;
        entity.Comp.Flowing = false;
        Dirty(entity);
        UpdateAppearance(entity);
        UserInterfaceSystem.CloseUi(entity.Owner, KsTemperatureGateUiKey.Key);
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<KsTemperatureGateComponent> entity, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(KsTemperatureGateComponent)))
        {
            args.PushMarkup(Loc.GetString("ks-temperature-gate-examined-status", ("enabled", entity.Comp.Enabled)));

            if (entity.Comp.Mode == KsTemperatureGateMode.Range)
            {
                args.PushMarkup(Loc.GetString("ks-temperature-gate-examined-range",
                    ("inverted", entity.Comp.Inverted),
                    ("minimum", $"{entity.Comp.RangeMinimum:0.#}"),
                    ("maximum", $"{entity.Comp.RangeMaximum:0.#}")));
            }
            else
            {
                args.PushMarkup(Loc.GetString("ks-temperature-gate-examined-tolerance",
                    ("inverted", entity.Comp.Inverted),
                    ("tolerance", $"{entity.Comp.Tolerance:0.#}"),
                    ("temperature", $"{entity.Comp.TargetTemperature:0.#}")));
            }
        }
    }

    [SubscribeLocalEvent]
    private void OnGetAlternativeVerbs(Entity<KsTemperatureGateComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        if (entity.Comp.Mode == KsTemperatureGateMode.Tolerance)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("ks-temperature-gate-verb-maximize"),
                Priority = 1,
                Disabled = entity.Comp.TargetTemperature >= entity.Comp.MaxTemperature,
                Act = () => SetTarget(entity, entity.Comp.MaxTemperature, user),
            });
        }

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(entity.Comp.Enabled ? "ks-temperature-gate-verb-off" : "ks-temperature-gate-verb-on"),
            Disabled = !_powerReceiverSystem.IsPowered(entity.Owner),
            Act = () => SetEnabled(entity, !entity.Comp.Enabled, user),
        });
    }

    [SubscribeLocalEvent]
    private void OnInteractUsing(Entity<KsTemperatureGateComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled || !_toolSystem.HasQuality(args.Used, entity.Comp.InvertQuality))
            return;

        args.Handled = true;
        entity.Comp.Inverted = !entity.Comp.Inverted;
        Dirty(entity);
        UpdateUi(entity);

        _adminLogManager.Add(LogType.AtmosDeviceSetting,
            LogImpact.Medium,
            $"{ToPrettyString(args.User):player} set {ToPrettyString(entity):device} to open {(entity.Comp.Inverted ? "above" : "below")} its target");
        _popupSystem.PopupEntity(KsPopupMessage.Create(entity.Comp.Inverted ? "ks-temperature-gate-inverted" : "ks-temperature-gate-restored"),
            entity,
            args.User);
    }

    // By value, as this event is a class: see "Entity<T>, not a uid and a component side by side" in CLAUDE.md.
    [SubscribeLocalEvent]
    private void OnUnanchorAttempt(EntityUid uid, KsTemperatureGateComponent component, UnanchorAttemptEvent args)
    {
        if (!component.Enabled || !_powerReceiverSystem.IsPowered(uid))
            return;

        args.Cancel();
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-temperature-gate-unanchor-on"), uid, args.User);
    }

    public void SetEnabled(Entity<KsTemperatureGateComponent> entity, bool enabled, EntityUid? user = null)
    {
        entity.Comp.Enabled = enabled;
        Dirty(entity);
        UpdateAppearance(entity);
        UpdateUi(entity);

        _adminLogManager.Add(LogType.AtmosPowerChanged,
            LogImpact.Medium,
            $"{ToPrettyString(user):player} set the power on {ToPrettyString(entity):device} to {enabled}");
    }

    public void SetTarget(Entity<KsTemperatureGateComponent> entity, float temperature, EntityUid? user = null)
    {
        entity.Comp.TargetTemperature = Math.Clamp(temperature, entity.Comp.MinTemperature, entity.Comp.MaxTemperature);
        Dirty(entity);
        UpdateUi(entity);

        _adminLogManager.Add(LogType.AtmosTemperatureChanged,
            LogImpact.Medium,
            $"{ToPrettyString(user):player} set the target on {ToPrettyString(entity):device} to {entity.Comp.TargetTemperature} K");
    }

    public void SetMode(Entity<KsTemperatureGateComponent> entity, KsTemperatureGateMode mode, EntityUid? user = null)
    {
        entity.Comp.Mode = mode;
        Dirty(entity);
        UpdateUi(entity);

        _adminLogManager.Add(LogType.AtmosDeviceSetting,
            LogImpact.Medium,
            $"{ToPrettyString(user):player} set {ToPrettyString(entity):device} to {mode} mode");
    }

    public void SetTolerance(Entity<KsTemperatureGateComponent> entity, float tolerance, EntityUid? user = null)
    {
        entity.Comp.Tolerance = Math.Clamp(tolerance, 0f, entity.Comp.MaxTemperature);
        Dirty(entity);
        UpdateUi(entity);

        _adminLogManager.Add(LogType.AtmosTemperatureChanged,
            LogImpact.Medium,
            $"{ToPrettyString(user):player} set the tolerance on {ToPrettyString(entity):device} to {entity.Comp.Tolerance} K");
    }

    /// <summary>
    ///     Clamps both ends, and takes them in either order, so the range can't end up empty.
    /// </summary>
    public void SetRange(Entity<KsTemperatureGateComponent> entity, float minimum, float maximum, EntityUid? user = null)
    {
        minimum = Math.Clamp(minimum, entity.Comp.MinTemperature, entity.Comp.MaxTemperature);
        maximum = Math.Clamp(maximum, entity.Comp.MinTemperature, entity.Comp.MaxTemperature);
        entity.Comp.RangeMinimum = MathF.Min(minimum, maximum);
        entity.Comp.RangeMaximum = MathF.Max(minimum, maximum);
        Dirty(entity);
        UpdateUi(entity);

        _adminLogManager.Add(LogType.AtmosTemperatureChanged,
            LogImpact.Medium,
            $"{ToPrettyString(user):player} set the range on {ToPrettyString(entity):device} to {entity.Comp.RangeMinimum}-{entity.Comp.RangeMaximum} K");
    }

    /// <summary>
    ///     Whether <paramref name="temperature"/> at the inlet leaves the gate open.
    /// </summary>
    protected static bool Passes(KsTemperatureGateComponent gate, float temperature)
    {
        var (minimum, maximum) = gate.Mode == KsTemperatureGateMode.Range
            ? (gate.RangeMinimum, gate.RangeMaximum)
            : (gate.TargetTemperature - gate.Tolerance, gate.TargetTemperature + gate.Tolerance);

        var inside = temperature >= minimum && temperature <= maximum;
        return inside != gate.Inverted;
    }

    /// <summary>
    ///     Only dirties when it changes, as the flow is checked on every atmos update.
    /// </summary>
    protected void SetFlowing(Entity<KsTemperatureGateComponent> entity, bool flowing)
    {
        if (entity.Comp.Flowing == flowing)
            return;

        entity.Comp.Flowing = flowing;
        Dirty(entity);
        UpdateAppearance(entity);
    }

    private void UpdateAppearance(Entity<KsTemperatureGateComponent> entity)
    {
        var state = KsTemperatureGateState.Off;
        if (entity.Comp.Enabled && _powerReceiverSystem.IsPowered(entity.Owner))
            state = entity.Comp.Flowing ? KsTemperatureGateState.Flowing : KsTemperatureGateState.On;

        _appearanceSystem.SetData(entity.Owner, KsTemperatureGateVisuals.State, state);
    }

    protected virtual void UpdateUi(Entity<KsTemperatureGateComponent> entity)
    {
    }
}
