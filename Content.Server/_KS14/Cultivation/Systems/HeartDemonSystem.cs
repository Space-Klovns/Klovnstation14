using Content.Shared._KS14.Cultivation.Body;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using System.Numerics;
using Content.Server._KS14.Cultivation.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Your doubts, given your face. A heart demon tears free from a failed breakthrough and hunts only you.
///     Beat it yourself (it has to die while you're standing close) and your Dao heart is tempered, steadying the
///         next breakthrough. Let it run out the clock and it climbs back inside you, much heavier than before.
/// </summary>
public sealed partial class HeartDemonSystem : EntitySystem
{
    [Dependency] private Content.Shared.NPC.Systems.NpcFactionSystem _npcFactionSystem = default!;
    private static readonly EntProtoId HeartDemonPrototype = "KsHeartDemon";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(90);
    private static readonly SoundSpecifier SummonSound = new SoundPathSpecifier("/Audio/Magic/disintegrate.ogg");
    private static readonly SoundSpecifier ShatterSound = new SoundPathSpecifier("/Audio/Effects/glass_crack1.ogg");

    /// <summary>
    ///     How close the host must be to their demon when it dies to count as having beaten it.
    /// </summary>
    private const float VictoryRange = 2.5f;

    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private JianghuSystem _jianghuSystem = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private NPCRetaliationSystem _npcRetaliationSystem = default!;
    [Dependency] private SharedCultivationSystem _cultivationSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _gameTiming.CurTime;
        var demonEnumerator = EntityQueryEnumerator<HeartDemonComponent>();
        while (demonEnumerator.MoveNext(out var demonUid, out var demonComponent))
        {
            if (currentTime < demonComponent.Expires || EntityManager.IsQueuedForDeletion(demonUid))
                continue;

            ReturnToHost((demonUid, demonComponent));
        }
    }

    /// <summary>
    ///     Pull a heart demon out of someone, unless one of theirs is already loose.
    /// </summary>
    public EntityUid? Summon(EntityUid hostUid, TimeSpan? lifetime = null)
    {
        var demonEnumerator = EntityQueryEnumerator<HeartDemonComponent>();
        while (demonEnumerator.MoveNext(out var existingUid, out var existingComponent))
        {
            if (existingComponent.Host == hostUid && !EntityManager.IsQueuedForDeletion(existingUid))
                return existingUid;
        }

        // Right where its host stands: an offset spot can be inside a wall or out in space, where it could never reach them.
        var spawnCoordinates = Transform(hostUid).Coordinates;
        var demonUid = Spawn(HeartDemonPrototype, spawnCoordinates);
        var demonComponent = EnsureComp<HeartDemonComponent>(demonUid);
        demonComponent.Host = hostUid;
        demonComponent.Expires = _gameTiming.CurTime + (lifetime ?? DefaultLifetime);

        _metaDataSystem.SetEntityName(demonUid, Loc.GetString("heart-demon-name", ("host", hostUid)));

        // The further along the path, the stronger the doubts: harder blows and a body that takes more killing.
        var tier = 0;
        if (_cultivatorQuery.TryComp(hostUid, out var hostCultivator))
            tier = (int)hostCultivator.Realm;
        else if (TryComp<BodyCultivatorComponent>(hostUid, out var hostBody))
            tier = hostBody.Stage / 3;

        demonComponent.BonusDamage = 6f + 8f * tier;
        _mobThresholdSystem.SetMobStateThreshold(demonUid, FixedPoint2.New(60 + 30 * tier), MobState.Critical);
        _mobThresholdSystem.SetMobStateThreshold(demonUid, FixedPoint2.New(100 + 40 * tier), MobState.Dead);
        // It hunts its host for as long as it lives: a lasting grudge, not retaliation that wears off.
        _npcFactionSystem.AggroEntity(demonUid, hostUid);
        if (TryComp<NPCRetaliationComponent>(demonUid, out var retaliationComponent))
            _npcRetaliationSystem.TryRetaliate((demonUid, retaliationComponent), hostUid);

        _audioSystem.PlayPvs(SummonSound, hostUid);
        _noticeSystem.Announce(hostUid, Loc.GetString("heart-demon-summoned", ("host", hostUid)), type: PopupType.LargeCaution);
        return demonUid;
    }

    [SubscribeLocalEvent]
    private void OnDemonMeleeHit(Entity<HeartDemonComponent> entity, ref MeleeHitEvent args)
    {
        if (args.IsHit && args.HitEntities.Count > 0 && entity.Comp.BonusDamage > 0f)
            args.BonusDamage += CultivationSystem.Damage("Blunt", entity.Comp.BonusDamage);
    }

    [SubscribeLocalEvent]
    private void OnMobStateChanged(Entity<HeartDemonComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var hostUid = entity.Comp.Host;
        QueueDel(entity);
        _audioSystem.PlayPvs(ShatterSound, Transform(entity).Coordinates);

        if (hostUid is not { } host || TerminatingOrDeleted(host))
            return;

        _noticeSystem.Announce(entity, Loc.GetString("heart-demon-shatters"), type: PopupType.Medium);
        if (!_transformSystem.InRange((entity.Owner, null), (host, null), VictoryRange))
        {
            _noticeSystem.Notify(host, Loc.GetString("heart-demon-not-yours"), type: PopupType.Medium);
            return;
        }

        if (_cultivatorQuery.HasComp(host))
            _cultivationSystem.SetDaoHeartTempered(host, true);

        _jianghuSystem.SectMissionProgress(host, SectMission.HeartDemon, 1);

        _noticeSystem.Notify(host, Loc.GetString("heart-demon-defeated"), type: PopupType.Large);
    }

    /// <summary>
    ///     Out of time: the demon climbs back inside, heavier than before.
    /// </summary>
    private void ReturnToHost(Entity<HeartDemonComponent> entity)
    {
        var hostUid = entity.Comp.Host;
        QueueDel(entity);
        if (hostUid is not { } host || TerminatingOrDeleted(host))
            return;

        if (_cultivatorQuery.TryComp(host, out var cultivatorComponent))
        {
            _cultivationSystem.AddInternalInjury((host, cultivatorComponent), stacks: 2);
            _cultivationSystem.ScaleInsight((host, cultivatorComponent), 0.8f);
            _cultivationSystem.SetDaoHeartTempered((host, cultivatorComponent), false);
        }

        _noticeSystem.Announce(host, Loc.GetString("heart-demon-returns", ("host", host)), type: PopupType.LargeCaution);
    }
}

/// <summary>
///     A heart demon, bound to the one whose doubts made it.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(HeartDemonSystem))]
public sealed partial class HeartDemonComponent : Component
{
    [DataField]
    public EntityUid? Host;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan Expires;

    /// <summary>
    ///     Extra blunt damage on each of its blows: a greater cultivator's doubts hit harder.
    /// </summary>
    [DataField]
    public float BonusDamage;
}
