using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Actions;
using Content.Shared.Damage.Systems;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Body Outside the Body: a Nascent Soul cultivator's soul steps out as a small golden figure for a while. It
///         flies over tables and crowds while the body sits empty. When time runs out, or the soul returns, the mind
///         goes back; if the soul is destroyed out there, the body pays for it.
/// </summary>
public sealed partial class NascentProjectionSystem : EntitySystem
{
    private static readonly EntProtoId SoulPrototype = "KsNascentSoul";
    private static readonly EntProtoId ReturnAction = "ActionCultivationReturnToBody";

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedMindSystem _mindSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _gameTiming.CurTime;
        var soulEnumerator = EntityQueryEnumerator<NascentProjectionComponent>();
        while (soulEnumerator.MoveNext(out var soulUid, out var projectionComponent))
        {
            if (currentTime >= projectionComponent.Expires && !EntityManager.IsQueuedForDeletion(soulUid))
                Return((soulUid, projectionComponent));
        }
    }

    [SubscribeLocalEvent]
    private void OnBodyOutsideBody(Entity<CultivatorComponent> entity, ref BodyOutsideBodyActionEvent args)
    {
        if (args.Handled || !_mindSystem.TryGetMind(entity, out var mindId, out _))
            return;

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        var soulUid = Spawn(SoulPrototype, Transform(entity).Coordinates);
        var projectionComponent = EnsureComp<NascentProjectionComponent>(soulUid);
        projectionComponent.Body = entity;
        projectionComponent.Expires = _gameTiming.CurTime + args.Duration;
        EnsureComp<SoulDepartedComponent>(entity).Soul = soulUid;

        _mindSystem.TransferTo(mindId, soulUid);
        _actionsSystem.AddAction(soulUid, ReturnAction);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-soul-departs", ("user", entity.Owner)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnReturnAction(Entity<NascentProjectionComponent> entity, ref ReturnToBodyActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Return(entity);
    }

    [SubscribeLocalEvent]
    private void OnSoulDied(Entity<NascentProjectionComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        // The soul was destroyed out there: it flees home, and the body bears the wound.
        if (entity.Comp.Body is { } bodyUid && !TerminatingOrDeleted(bodyUid))
            _damageableSystem.TryChangeDamage(bodyUid, CultivationSystem.Damage("Cellular", 30f), ignoreResistances: true);

        Return(entity);
    }

    [SubscribeLocalEvent]
    private void OnBodyDied(Entity<SoulDepartedComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive && entity.Comp.Soul is { } soulUid && TryComp<NascentProjectionComponent>(soulUid, out var projectionComponent))
            Return((soulUid, projectionComponent));
    }

    /// <summary>
    ///     Bring the soul home: the mind returns to the body and the golden figure fades.
    /// </summary>
    public void Return(Entity<NascentProjectionComponent> entity)
    {
        var bodyUid = entity.Comp.Body;
        if (_mindSystem.TryGetMind(entity, out var mindId, out _) && bodyUid is { } body && !TerminatingOrDeleted(body))
        {
            _mindSystem.TransferTo(mindId, body);
            RemComp<SoulDepartedComponent>(body);
            _noticeSystem.Announce(body, Loc.GetString("cultivation-soul-returns", ("user", body)), type: PopupType.Medium);
        }

        QueueDel(entity);
    }
}

/// <summary>
///     A nascent soul walking outside its body.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(NascentProjectionSystem))]
public sealed partial class NascentProjectionComponent : Component
{
    [DataField]
    public EntityUid? Body;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan Expires;
}

/// <summary>
///     A body whose soul is out walking.
/// </summary>
[RegisterComponent, Access(typeof(NascentProjectionSystem))]
public sealed partial class SoulDepartedComponent : Component
{
    [DataField]
    public EntityUid? Soul;
}
