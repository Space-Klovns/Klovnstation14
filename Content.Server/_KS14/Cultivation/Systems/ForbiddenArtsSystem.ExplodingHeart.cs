using Content.Server._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cultivation.Systems;

// The Five-Point Exploding Heart Palm: five fingers around the heart, and after so many steps it bursts. Standing still
// is safe. Acupoint Sealing undoes it, and so do two cycles of meditation.
public sealed partial class ForbiddenArtsSystem
{
    /// <summary>
    ///     How much of a hundred blunt the chest's protection has to stop for the fingers to be turned aside.
    /// </summary>
    private const float ArmorTurnsFingers = 0.4f;

    private static readonly TimeSpan StainDuration = TimeSpan.FromMinutes(5);

    [Dependency] private AlertsSystem _alertsSystem = default!;

    /// <summary>
    ///     Hearts whose count ran out during a move, burst in Update.
    /// </summary>
    private readonly HashSet<EntityUid> _pendingBursts = new();
    private readonly List<EntityUid> _drainedBursts = new();

    [SubscribeLocalEvent]
    private void OnExplodingHeartPalm(Entity<CultivatorComponent> entity, ref ExplodingHeartPalmActionEvent args)
    {
        if (args.Handled)
            return;

        var victimUid = args.Target;
        if (_mobStateSystem.IsDead(victimUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-exploding-heart-dead"));
            return;
        }

        // An honor duel is fought with honor.
        if (HasComp<HonorDuelComponent>(entity) || HasComp<HonorDuelComponent>(victimUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-exploding-heart-duel"));
            return;
        }

        if (HasComp<ExplodingHeartComponent>(victimUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-exploding-heart-already"));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtPalmBlood, args.Target);
        _visualsSystem.Strike(entity, args.Target, FxColour.Blood);
        AddTaint(entity, 6f);

        if (ChestTurnsFingers(victimUid))
        {
            _noticeSystem.Announce(victimUid, Loc.GetString("cultivation-exploding-heart-armored", ("user", entity.Owner), ("target", victimUid)), type: PopupType.Medium);
            return;
        }

        StrikeExplodingHeart(entity, victimUid, args.BaseSteps);
    }

    /// <summary>
    ///     Plant the five points: the heart bursts after the base number of steps, one more for every realm the
    ///         victim stands above the striker, and one more for every three levels of a tempered heart.
    /// </summary>
    public void StrikeExplodingHeart(Entity<CultivatorComponent> striker, EntityUid victimUid, int baseSteps)
    {
        var steps = baseSteps;
        if (_cultivatorQuery.TryComp(victimUid, out var victimCultivator))
            steps += Math.Max(0, (int)victimCultivator.Realm - (int)striker.Comp.Realm);
        if (TryComp<BodyCultivatorComponent>(victimUid, out var victimBody))
            steps += SharedBodyCultivationSystem.GetLevel(victimBody, BodyGroup.Heart) / 3;

        var heartComponent = EnsureComp<ExplodingHeartComponent>(victimUid);
        heartComponent.StepsLeft = steps;
        heartComponent.Caster = striker;
        heartComponent.CasterName = Name(striker);
        heartComponent.CalmCycles = 0;
        heartComponent.LastTile = _transformSystem.GetGridOrMapTilePosition(victimUid);
        Dirty(victimUid, heartComponent);
        _alertsSystem.ShowAlert(victimUid, heartComponent.Alert);

        EnsureComp<ExplodingHeartStainComponent>(striker).ExpiresAt = _gameTiming.CurTime + StainDuration;

        _noticeSystem.Announce(victimUid, Loc.GetString("cultivation-exploding-heart-struck", ("user", striker.Owner), ("target", victimUid)), type: PopupType.MediumCaution);
        _noticeSystem.Notify(victimUid, Loc.GetString("cultivation-exploding-heart-victim"), type: PopupType.LargeCaution);
        _noticeSystem.Notify(striker, Loc.GetString("cultivation-exploding-heart-planted", ("target", victimUid), ("steps", steps)));
        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(striker):player} struck {ToPrettyString(victimUid):target} with the Five-Point Exploding Heart Palm ({steps} steps)");
    }

    /// <summary>
    ///     Test the chest's protection against a heavy blow, the way the fingers would land.
    /// </summary>
    private bool ChestTurnsFingers(EntityUid victimUid)
    {
        var probeDamage = CultivationSystem.Damage("Blunt", 100);
        var modifyEvent = new DamageModifyEvent(probeDamage);
        RaiseLocalEvent(victimUid, modifyEvent);
        return modifyEvent.Damage.GetTotal().Float() <= 100f * (1f - ArmorTurnsFingers);
    }

    /// <summary>
    ///     Only steps the victim takes themselves count: being dragged or thrown doesn't.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnHeartMoved(Entity<ExplodingHeartComponent> entity, ref MoveEvent args)
    {
        if (entity.Comp.StepsLeft <= 0 || TerminatingOrDeleted(entity))
            return;

        var tilePosition = _transformSystem.GetGridOrMapTilePosition(entity, args.Component);
        if (tilePosition == entity.Comp.LastTile)
            return;

        entity.Comp.LastTile = tilePosition;
        if (args.ParentChanged || HasComp<ThrownItemComponent>(entity)
            || TryComp<PullableComponent>(entity, out var pullableComponent) && pullableComponent.BeingPulled)
            return;

        entity.Comp.StepsLeft--;
        Dirty(entity);

        // The burst knocks the victim down and spills what they hold, which is no work for inside a move: it waits
        //      for Update.
        if (entity.Comp.StepsLeft <= 0)
        {
            _pendingBursts.Add(entity.Owner);
            return;
        }

        if (entity.Comp.StepsLeft <= 2)
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-exploding-heart-pounding", ("steps", entity.Comp.StepsLeft)), type: PopupType.LargeCaution);
        }
    }

    private void BurstHeart(Entity<ExplodingHeartComponent> entity)
    {
        var victimUid = entity.Owner;
        var casterName = entity.Comp.CasterName;
        var casterUid = entity.Comp.Caster;
        RemComp<ExplodingHeartComponent>(victimUid);

        _visualsSystem.Wave(victimUid, FxColour.Blood, big: true);
        _visualsSystem.Spark(victimUid, FxColour.Blood);
        var burstDamage = CultivationSystem.Damage("Blunt", 35f);
        burstDamage.DamageDict.Add("Piercing", FixedPoint2.New(35));
        burstDamage.DamageDict.Add("Bloodloss", FixedPoint2.New(20));
        _damageableSystem.TryChangeDamage(victimUid, _cultivationSystem.Empowered(casterUid, burstDamage), ignoreResistances: true, origin: casterUid);
        _bloodstreamSystem.TryModifyBleedAmount(victimUid, 6f);
        _stunSystem.TryKnockdown(victimUid, TimeSpan.FromSeconds(3), refresh: true);
        _sharedCultivationSystem.AddInternalInjury(victimUid, stacks: 2);
        _audioSystem.PlayPvs(CurseSound, victimUid);

        _noticeSystem.Announce(victimUid, Loc.GetString("cultivation-exploding-heart-burst", ("target", victimUid)), type: PopupType.LargeCaution);
        if (!_mobStateSystem.IsDead(victimUid))
            _noticeSystem.Notify(victimUid, Loc.GetString("cultivation-exploding-heart-remember", ("caster", casterName)), type: PopupType.Large);

        _adminLogger.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(victimUid):player}'s heart burst from the Five-Point Exploding Heart Palm (struck by {casterName})");
    }

    /// <summary>
    ///     The points dissolve. The victim always learns who struck them.
    /// </summary>
    public void UnsealHeart(Entity<ExplodingHeartComponent> entity, string how)
    {
        var casterName = entity.Comp.CasterName;
        RemComp<ExplodingHeartComponent>(entity);
        _noticeSystem.Announce(entity, Loc.GetString(how, ("target", entity.Owner)), type: PopupType.Medium);
        _noticeSystem.Notify(entity, Loc.GetString("cultivation-exploding-heart-remember", ("caster", casterName)), type: PopupType.Large);
    }

    [SubscribeLocalEvent]
    private void OnHeartSealed(Entity<ExplodingHeartComponent> entity, ref AcupointSealedEvent args)
    {
        args.Handled = true;
        UnsealHeart(entity, "cultivation-exploding-heart-unsealed");
    }

    [SubscribeLocalEvent]
    private void OnHeartMeditated(Entity<ExplodingHeartComponent> entity, ref CultivationMeditatedEvent args)
    {
        entity.Comp.CalmCycles++;
        if (entity.Comp.CalmCycles >= 2)
        {
            UnsealHeart(entity, "cultivation-exploding-heart-meditated");
            return;
        }

        _noticeSystem.Notify(entity, Loc.GetString("cultivation-exploding-heart-calming"));
    }

    [SubscribeLocalEvent]
    private void OnHeartRemoved(Entity<ExplodingHeartComponent> entity, ref ComponentRemove args)
    {
        _alertsSystem.ClearAlert(entity.Owner, entity.Comp.Alert);
    }

    [SubscribeLocalEvent]
    private void OnHeartExamined(Entity<ExplodingHeartComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("cultivation-exploding-heart-examine", ("target", entity.Owner)));
    }

    [SubscribeLocalEvent]
    private void OnHeartSensed(Entity<ExplodingHeartComponent> entity, ref CultivationSensedEvent args)
    {
        SensePopup(entity, args.Sensor, "cultivation-sense-exploding-heart", ref args);
    }

    [SubscribeLocalEvent]
    private void OnStainSensed(Entity<ExplodingHeartStainComponent> entity, ref CultivationSensedEvent args)
    {
        SensePopup(entity, args.Sensor, "cultivation-sense-heart-stain", ref args);
    }

    private void UpdateBursts()
    {
        _drainedBursts.Clear();
        _drainedBursts.AddRange(_pendingBursts);
        _pendingBursts.Clear();
        foreach (var victimUid in _drainedBursts)
        {
            if (TryComp<ExplodingHeartComponent>(victimUid, out var heartComponent) && heartComponent.StepsLeft <= 0)
                BurstHeart((victimUid, heartComponent));
        }
    }

    private void UpdateStains(TimeSpan now)
    {
        var stainQuery = EntityQueryEnumerator<ExplodingHeartStainComponent>();
        while (stainQuery.MoveNext(out var uid, out var stainComponent))
        {
            if (now >= stainComponent.ExpiresAt)
                RemCompDeferred<ExplodingHeartStainComponent>(uid);
        }
    }
}
