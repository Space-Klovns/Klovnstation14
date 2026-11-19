using Content.Shared._KS14.Companions;
using Content.Shared._KS14.Perks.Support;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Alert;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Perks.Support;

/// <summary>
///     Rally (<see cref="KsRallyAbilityComponent"/>): the call puts down a ring, and when it goes off every living friend
///         of the caller inside it is rallied (<see cref="KsRallyBuffComponent"/>) for a while, with a sound, a popup and
///         an alert counting it down; it wearing off is told the same way.
/// </summary>
public sealed partial class KsRallySystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AlertsSystem _alertsSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private KsRallyBuffSystem _rallyBuffSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsCompanionComponent> _companionQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;

    private readonly HashSet<Entity<MobStateComponent>> _inZone = new();

    /// <summary>
    ///     Starts the countdown of any zone, called or not. Without it a zone spawned by anything but
    ///         <see cref="Call"/> has its activate time at zero, so it goes off on its first update and is deleted on the
    ///         next, after the clients were told it exists but before they hear it is gone.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnZoneMapInit(Entity<KsRallyZoneComponent> zone, ref MapInitEvent args)
    {
        var curTime = _gameTiming.CurTime;
        zone.Comp.StartTime = curTime;
        zone.Comp.ActivateTime = curTime + zone.Comp.Delay;
        Dirty(zone);
    }

    [SubscribeLocalEvent]
    private void OnAction(Entity<KsRallyAbilityComponent> entity, ref KsRallyActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Call(entity);
    }

    /// <summary>
    ///     Calls a rally where <paramref name="caller"/> stands. Returns the zone.
    /// </summary>
    public EntityUid Call(Entity<KsRallyAbilityComponent> caller)
    {
        var curTime = _gameTiming.CurTime;
        var zoneUid = Spawn(caller.Comp.Zone, _transformSystem.GetMapCoordinates(caller.Owner));
        var zoneComponent = EnsureComp<KsRallyZoneComponent>(zoneUid);
        zoneComponent.Radius = caller.Comp.Radius;
        zoneComponent.StartTime = curTime;
        zoneComponent.ActivateTime = curTime + caller.Comp.Delay;
        zoneComponent.CallerUid = caller.Owner;
        zoneComponent.Factions = _factionMemberQuery.TryComp(caller.Owner, out var factionMemberComponent)
            ? new HashSet<ProtoId<NpcFactionPrototype>>(factionMemberComponent.Factions)
            : new HashSet<ProtoId<NpcFactionPrototype>>();
        zoneComponent.Duration = caller.Comp.Duration;
        zoneComponent.SpeedMultiplier = caller.Comp.SpeedMultiplier;
        zoneComponent.FireRateMultiplier = caller.Comp.FireRateMultiplier;
        zoneComponent.DamageMultiplier = caller.Comp.DamageMultiplier;
        zoneComponent.Alert = caller.Comp.Alert;
        zoneComponent.RallySound = caller.Comp.RallySound;
        zoneComponent.EndSound = caller.Comp.EndSound;
        Dirty(zoneUid, zoneComponent);

        _audioSystem.PlayPvs(caller.Comp.CallSound, caller.Owner);
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-support-rally-called", ("user", caller.Owner)), caller.Owner, PopupType.Medium);
        return zoneUid;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        var zoneQuery = EntityQueryEnumerator<KsRallyZoneComponent, TransformComponent>();
        while (zoneQuery.MoveNext(out var zoneUid, out var zoneComponent, out var transformComponent))
        {
            if (zoneComponent.Activated)
            {
                // Gone once it has faded.
                if (curTime >= zoneComponent.ActivateTime + zoneComponent.FadeDuration)
                    QueueDel(zoneUid);

                continue;
            }

            if (curTime < zoneComponent.ActivateTime)
                continue;

            zoneComponent.Activated = true;
            Activate((zoneUid, zoneComponent), transformComponent, curTime);
        }

        var buffQuery = EntityQueryEnumerator<KsRallyBuffComponent>();
        while (buffQuery.MoveNext(out var uid, out var buffComponent))
        {
            if (buffComponent.EndTime > curTime || buffComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            if (buffComponent.Alert is { } alert)
                _alertsSystem.ClearAlert(uid, alert);

            _audioSystem.PlayEntity(buffComponent.EndSound, uid, uid);
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-support-rally-ended"), uid, uid, PopupType.SmallCaution);
            RemCompDeferred<KsRallyBuffComponent>(uid);
        }
    }

    private void Activate(Entity<KsRallyZoneComponent> zone, TransformComponent transformComponent, TimeSpan curTime)
    {
        _audioSystem.PlayPvs(zone.Comp.RallySound, zone.Owner);

        var endTime = curTime + zone.Comp.Duration;
        _inZone.Clear();
        _entityLookupSystem.GetEntitiesInRange(transformComponent.Coordinates, zone.Comp.Radius, _inZone);
        foreach (var mob in _inZone)
        {
            if (mob.Comp.CurrentState != MobState.Alive ||
                _companionQuery.HasComp(mob.Owner) ||
                (mob.Owner != zone.Comp.CallerUid &&
                 !(_factionMemberQuery.TryComp(mob.Owner, out var factionMemberComponent) &&
                   zone.Comp.Factions.Overlaps(factionMemberComponent.Factions))))
                continue;

            var buffComponent = _rallyBuffSystem.Apply(mob.Owner, endTime, zone.Comp.SpeedMultiplier,
                zone.Comp.FireRateMultiplier, zone.Comp.DamageMultiplier);
            _rallyBuffSystem.SetTells((mob.Owner, buffComponent), zone.Comp.Alert, zone.Comp.EndSound);

            if (zone.Comp.Alert is { } alert)
                _alertsSystem.ShowAlert(mob.Owner, alert, cooldown: (curTime, buffComponent.EndTime));

            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-perk-support-rallied"), mob.Owner, mob.Owner, PopupType.Medium);
        }
    }
}
