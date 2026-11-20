using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Alert;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Perks.Tank;

/// <summary>
///     Injects <see cref="Injection"/> into the bloodstream on going critical, then waits out <see cref="Cooldown"/>,
///         shown on <see cref="CooldownAlert"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(KsCritAutoInjectorSystem))]
public sealed partial class KsCritAutoInjectorAbilityComponent : Component
{
    [DataField]
    public Solution Injection = new();

    [DataField, AutoNetworkedField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(3);

    /// <summary>
    ///     The alert the cooldown is drawn over; the perk's own, so it needs no icon of its own.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype>? CooldownAlert;

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");

    /// <summary>
    ///     When it can fire again.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextReady;
}

public sealed partial class KsCritAutoInjectorSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AlertsSystem _alertsSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    // A by-value event, so no Entity<T> handler (RA0054).
    [SubscribeLocalEvent]
    private void OnMobStateChanged(EntityUid uid, KsCritAutoInjectorAbilityComponent component, MobStateChangedEvent args)
    {
        // Injecting is the server's: a mob's state change isn't predicted.
        if (_netManager.IsClient ||
            args.NewMobState != MobState.Critical ||
            _gameTiming.CurTime < component.NextReady)
            return;

        if (!_bloodstreamSystem.TryAddToBloodstream(uid, component.Injection.Clone()))
            return;

        var curTime = _gameTiming.CurTime;
        component.NextReady = curTime + component.Cooldown;
        Dirty(uid, component);

        if (component.CooldownAlert is { } alert)
            _alertsSystem.ShowAlert(uid, alert, cooldown: (curTime, component.NextReady));

        _audioSystem.PlayPvs(component.Sound, uid);
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-tank-auto-injector", ("user", uid)), uid, PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsCritAutoInjectorAbilityComponent> entity, ref ComponentShutdown args)
    {
        // The alert may stay (another perk's, or this perk still held): take the cooldown off it.
        if (_netManager.IsClient ||
            TerminatingOrDeleted(entity.Owner) ||
            entity.Comp.CooldownAlert is not { } alert ||
            !_alertsSystem.IsShowingAlert(entity.Owner, alert))
            return;

        _alertsSystem.ShowAlert(entity.Owner, alert);
    }
}
