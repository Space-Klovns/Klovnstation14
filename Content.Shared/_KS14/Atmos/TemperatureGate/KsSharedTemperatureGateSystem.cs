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
            args.PushMarkup(Loc.GetString("ks-temperature-gate-examined",
                ("enabled", entity.Comp.Enabled),
                ("inverted", entity.Comp.Inverted),
                ("temperature", $"{entity.Comp.TargetTemperature:0.#}")));
        }
    }

    [SubscribeLocalEvent]
    private void OnGetAlternativeVerbs(Entity<KsTemperatureGateComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ks-temperature-gate-verb-maximize"),
            Priority = 1,
            Disabled = entity.Comp.TargetTemperature >= entity.Comp.MaxTemperature,
            Act = () => SetTarget(entity, entity.Comp.MaxTemperature, user),
        });

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
