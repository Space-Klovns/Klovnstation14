using System.Linq;
using System.Text;
using Content.Server._KS14.Cultivation.Components;
using Content.Server.Chat.Managers;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Jittering;
using Content.Shared.Maps;
using Content.Shared.Medical;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Stunnable;
using Content.Shared.Traits.Assorted;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The Demonic Path: a forbidden scripture only those already sworn against the station can read, and the arts it
///         teaches. Devouring the dead, gu worms, soul search, corpse puppets, the blood escape and the Five-Point
///         Exploding Heart Palm. A master can transmit it once per disciple; a disciple can never pass it on.
/// </summary>
public sealed partial class ForbiddenArtsSystem : EntitySystem
{
    /// <summary>
    ///     The forbidden arts, by the realm that opens them.
    /// </summary>
    public static readonly (EntProtoId Action, CultivationRealm Realm)[] ForbiddenTechniques =
    {
        ("ActionCultivationDevouringArt", CultivationRealm.QiCondensation),
        ("ActionCultivationGuWorm", CultivationRealm.QiCondensation),
        ("ActionCultivationStirGu", CultivationRealm.QiCondensation),
        ("ActionCultivationSoulSearch", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationCorpsePuppet", CultivationRealm.GoldenCore),
        ("ActionCultivationBloodEscape", CultivationRealm.GoldenCore),
        ("ActionCultivationExplodingHeartPalm", CultivationRealm.GoldenCore),
    };

    /// <summary>
    ///     Only a master holds the scripture in their heart, and only a master can pass it on.
    /// </summary>
    public static readonly EntProtoId TransmitAction = "ActionCultivationTransmitDemonicArt";

    private static readonly EntProtoId TransmissionPrototype = "KsDemonicTransmission";
    /// <summary>
    ///     The wounds a devoured corpse closes: about twenty of brute and twenty of burn.
    /// </summary>
    private static readonly (string Type, float Amount)[] DevourHealing =
    {
        ("Blunt", 18f), ("Slash", 18f), ("Piercing", 15f), ("Heat", 20f), ("Shock", 10f), ("Caustic", 10f), ("Poison", 10f),
    };
    private static readonly SoundSpecifier CurseSound = new SoundPathSpecifier("/Audio/Magic/disintegrate.ogg");
    private static readonly TimeSpan TaintDecayInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan GuSipInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Reagents a gu worm can't stand. A dose in the blood drives it out.
    /// </summary>
    private static readonly string[] GuBane = { "Holywater" };

    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;
    [Dependency] private PullingSystem _pullingSystem = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedCuffableSystem _cuffableSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedJitteringSystem _jitteringSystem = default!;
    [Dependency] private SharedJobSystem _jobSystem = default!;
    [Dependency] private SharedMindSystem _mindSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private SharedRoleSystem _roleSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private VomitSystem _vomitSystem = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;
    [Dependency] private EntityQuery<DemonicPathComponent> _demonicQuery = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;

        var demonicQuery = EntityQueryEnumerator<DemonicPathComponent>();
        while (demonicQuery.MoveNext(out _, out var demonicComponent))
        {
            if (now < demonicComponent.NextDecay)
                continue;

            demonicComponent.NextDecay = now + TaintDecayInterval;
            demonicComponent.Taint = Math.Max(0f, demonicComponent.Taint - demonicComponent.TaintDecay);
        }

        var wormQuery = EntityQueryEnumerator<GuWormComponent>();
        while (wormQuery.MoveNext(out var hostUid, out var wormComponent))
        {
            if (now >= wormComponent.ExpiresAt || _mobStateSystem.IsDead(hostUid))
            {
                RemCompDeferred<GuWormComponent>(hostUid);
                continue;
            }

            if (now < wormComponent.NextSip)
                continue;

            wormComponent.NextSip = now + GuSipInterval;
            if (HasGuBane(hostUid))
            {
                ExpelWorm((hostUid, wormComponent));
                continue;
            }

            _damageableSystem.TryChangeDamage(hostUid, CultivationSystem.Damage("Poison", 2f), interruptsDoAfters: false);
            if (wormComponent.Master is { } masterUid && !TerminatingOrDeleted(masterUid))
            {
                _sharedCultivationSystem.AddQi(masterUid, 4f);
                _sharedCultivationSystem.AddInsight(masterUid, 1f);
            }
        }

        UpdateBursts();
        UpdatePuppets(now);
        UpdateStains(now);
    }

    #region The path itself

    /// <summary>
    ///     Set someone on the Demonic Path, awakening them first if they're a mortal.
    /// </summary>
    public void MakeDemonic(EntityUid uid, bool master)
    {
        var cultivatorComponent = EnsureComp<CultivatorComponent>(uid);
        var demonicComponent = EnsureComp<DemonicPathComponent>(uid);
        demonicComponent.Master |= master;
        _cultivationSystem.GrantTechniques((uid, cultivatorComponent));
    }

    public bool IsDemonic(EntityUid uid)
    {
        return _demonicQuery.HasComp(uid);
    }

    /// <summary>
    ///     Every forbidden art taints you a little, and heaven notices at the next breakthrough.
    /// </summary>
    public void AddTaint(EntityUid uid, float amount)
    {
        if (!_demonicQuery.TryComp(uid, out var demonicComponent))
            return;

        demonicComponent.Taint += amount;
        _visualsSystem.Cast(uid, FxColour.Blood);
    }

    [SubscribeLocalEvent]
    private void OnBreakthroughReadiness(Entity<DemonicPathComponent> entity, ref CultivationBreakthroughReadinessEvent args)
    {
        args.Readiness -= Math.Min((int)entity.Comp.Taint, entity.Comp.MaxReadinessPenalty);
    }

    [SubscribeLocalEvent]
    private void OnDemonicSensed(Entity<DemonicPathComponent> entity, ref CultivationSensedEvent args)
    {
        SensePopup(entity, args.Sensor, "cultivation-sense-demonic", ref args);
    }

    [SubscribeLocalEvent]
    private void OnGuSensed(Entity<GuWormComponent> entity, ref CultivationSensedEvent args)
    {
        SensePopup(entity, args.Sensor, "cultivation-sense-gu", ref args);
    }

    [SubscribeLocalEvent]
    private void OnJiangshiSensed(Entity<JiangshiComponent> entity, ref CultivationSensedEvent args)
    {
        SensePopup(entity, args.Sensor, "cultivation-sense-jiangshi", ref args);
    }

    private void SensePopup(EntityUid targetUid, EntityUid sensorUid, string message, ref CultivationSensedEvent args)
    {
        _popupSystem.PopupEntity(Loc.GetString(message, ("target", targetUid)), targetUid, sensorUid, type: PopupType.MediumCaution);
        args.Found = true;
    }

    #endregion

    #region The scripture

    [SubscribeLocalEvent]
    private void OnScriptureExamined(Entity<DemonicScriptureComponent> entity, ref ExaminedEvent args)
    {
        if (entity.Comp.Spent)
            args.PushMarkup(Loc.GetString("cultivation-scripture-spent"));
        else if (_cultivatorQuery.HasComp(args.Examiner))
            args.PushMarkup(Loc.GetString("cultivation-scripture-examine"));
    }

    [SubscribeLocalEvent]
    private void OnScriptureUsed(Entity<DemonicScriptureComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var userUid = args.User;

        if (entity.Comp.Spent)
        {
            _noticeSystem.Notify(userUid, Loc.GetString("cultivation-scripture-spent"));
            return;
        }

        if (_demonicQuery.TryComp(userUid, out var demonicComponent) && (demonicComponent.Master || entity.Comp.Transmission))
        {
            _noticeSystem.Notify(userUid, Loc.GetString("cultivation-scripture-known"));
            return;
        }

        if (TryComp<BodyCultivatorComponent>(userUid, out var bodyComponent) && bodyComponent.Committed)
        {
            _noticeSystem.Notify(userUid, Loc.GetString("cultivation-scripture-flesh"));
            return;
        }

        // The scripture itself only opens to those already sworn against the station. A master's transmission was
        // written for whoever accepts it.
        if (!entity.Comp.Transmission && !IsAntagonist(userUid))
        {
            ScriptureDeviation(userUid);
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, userUid, entity.Comp.ReadDuration, new DemonicScriptureDoAfterEvent(), entity, used: entity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            BreakOnDropItem = true,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
            _noticeSystem.Announce(userUid, Loc.GetString("cultivation-scripture-start", ("user", userUid), ("book", entity.Owner)), type: PopupType.SmallCaution);
    }

    private bool IsAntagonist(EntityUid uid)
    {
        return _mindSystem.TryGetMind(uid, out var mindId, out _) && _roleSystem.MindIsAntagonist(mindId);
    }

    /// <summary>
    ///     What happens to the righteous, or the merely curious, who read the scripture.
    /// </summary>
    private void ScriptureDeviation(EntityUid userUid)
    {
        _damageableSystem.TryChangeDamage(userUid, CultivationSystem.Damage("Blunt", 10f), ignoreResistances: true);
        _jitteringSystem.DoJitter(userUid, TimeSpan.FromSeconds(10), refresh: true);
        _sharedCultivationSystem.AddInternalInjury(userUid);
        _audioSystem.PlayPvs(CurseSound, userUid);
        _noticeSystem.Announce(userUid, Loc.GetString("cultivation-scripture-deviation", ("user", userUid)), type: PopupType.LargeCaution);
    }

    [SubscribeLocalEvent]
    private void OnScriptureRead(Entity<DemonicScriptureComponent> entity, ref DemonicScriptureDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || entity.Comp.Spent)
            return;

        args.Handled = true;
        var master = !entity.Comp.Transmission;
        MakeDemonic(args.User, master);
        _audioSystem.PlayPvs(CurseSound, args.User);
        _noticeSystem.Notify(args.User, Loc.GetString(master ? "cultivation-demonic-master" : "cultivation-demonic-disciple"), type: PopupType.Large);
        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(args.User):player} took up the Demonic Path as a {(master ? "master" : "disciple")}");

        // Whatever lived in the pages has moved into someone's heart.
        entity.Comp.Spent = true;
        if (entity.Comp.Transmission)
            QueueDel(entity);
    }

    [SubscribeLocalEvent]
    private void OnTransmitDemonicArt(Entity<CultivatorComponent> entity, ref TransmitDemonicArtActionEvent args)
    {
        if (args.Handled)
            return;

        if (!_demonicQuery.TryComp(entity, out var demonicComponent) || !demonicComponent.Master)
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-demonic-not-master"));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtBloodVortex, entity);
        var transmissionUid = Spawn(TransmissionPrototype, Transform(entity).Coordinates);
        _handsSystem.PickupOrDrop(entity, transmissionUid, checkActionBlocker: false);
        _sharedCultivationSystem.AddInsight(entity.AsNullable(), 10f);
        AddTaint(entity, 4f);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-demonic-transmit", ("user", entity.Owner)), type: PopupType.MediumCaution);
    }

    #endregion

    #region Devouring Art

    [SubscribeLocalEvent]
    private void OnDevouringArt(Entity<CultivatorComponent> entity, ref DevouringArtActionEvent args)
    {
        if (args.Handled)
            return;

        var victimUid = args.Target;
        if (_mobStateSystem.IsAlive(victimUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-devour-resists"));
            return;
        }

        if (HasComp<DevouredComponent>(victimUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-devour-husk"));
            return;
        }

        var duration = TimeSpan.FromSeconds(_mobStateSystem.IsDead(victimUid) ? 6 : 4);
        var doAfterArgs = new DoAfterArgs(EntityManager, entity, duration, new DevouringArtDoAfterEvent(), entity, target: victimUid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            DistanceThreshold = 1.5f,
        };

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost) || !_doAfterSystem.TryStartDoAfter(doAfterArgs))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtBloodVortex, args.Target);
        _visualsSystem.Strike(entity, args.Target, FxColour.Blood);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-devour-start", ("user", entity.Owner), ("target", victimUid)), type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnDevouringDone(Entity<CultivatorComponent> entity, ref DevouringArtDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } victimUid || HasComp<DevouredComponent>(victimUid))
            return;

        args.Handled = true;
        AddTaint(entity, 5f);
        _cultivatorQuery.TryComp(victimUid, out var victimCultivator);

        // Someone dying gives up a little of their life and qi.
        if (!_mobStateSystem.IsDead(victimUid))
        {
            var drainDamage = CultivationSystem.Damage("Blunt", 25f);
            drainDamage.DamageDict.Add("Asphyxiation", FixedPoint2.New(25));
            _damageableSystem.TryChangeDamage(victimUid, _cultivationSystem.Empowered(entity, drainDamage), ignoreResistances: true, origin: entity);
            if (victimCultivator != null)
            {
                var stolen = Math.Min(victimCultivator.Qi, 20f);
                _sharedCultivationSystem.AddQi((victimUid, victimCultivator), -stolen);
                _sharedCultivationSystem.AddQi(entity.AsNullable(), stolen);
            }

            _sharedCultivationSystem.AddInsight(entity.AsNullable(), 10f);
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-devour-drained", ("target", victimUid)));
            _adminLogger.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(entity):player} drained {ToPrettyString(victimUid):target} with the Devouring Art");
            return;
        }

        // A corpse gives up everything, and what's left can't be revived, only cloned.
        EnsureComp<DevouredComponent>(victimUid);
        var unrevivableComponent = EnsureComp<UnrevivableComponent>(victimUid);
        unrevivableComponent.Cloneable = true;
        unrevivableComponent.ReasonMessage = "cultivation-devoured-unrevivable";
        Dirty(victimUid, unrevivableComponent);

        var healingDamage = new DamageSpecifier();
        foreach (var (damageType, amount) in DevourHealing)
            healingDamage.DamageDict.Add(damageType, FixedPoint2.New(-amount));
        _damageableSystem.TryChangeDamage(entity.Owner, healingDamage, ignoreResistances: true, interruptsDoAfters: false);
        _sharedCultivationSystem.AddQi(entity.AsNullable(), 60f);

        var insight = 30f;
        if (victimCultivator != null)
        {
            // A dead cultivator's cultivation is the richest meal of all.
            var stolenInsight = victimCultivator.Insight / 3f;
            _sharedCultivationSystem.ScaleInsight((victimUid, victimCultivator), 0.75f);
            insight += stolenInsight;
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-devour-cultivator", ("target", victimUid)), type: PopupType.MediumCaution);
        }

        _sharedCultivationSystem.AddInsight(entity.AsNullable(), insight);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-devour-corpse", ("user", entity.Owner), ("target", victimUid)), type: PopupType.MediumCaution);
        _adminLogger.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(entity):player} devoured the corpse of {ToPrettyString(victimUid):target}");
    }

    [SubscribeLocalEvent]
    private void OnDevouredExamined(Entity<DevouredComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("cultivation-devoured-examine", ("target", entity.Owner)));
    }

    #endregion

    #region Gu worms

    [SubscribeLocalEvent]
    private void OnGuWorm(Entity<CultivatorComponent> entity, ref GuWormActionEvent args)
    {
        if (args.Handled)
            return;

        var hostUid = args.Target;
        if (_mobStateSystem.IsDead(hostUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-gu-dead"));
            return;
        }

        if (HasComp<GuWormComponent>(hostUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-gu-already", ("target", hostUid)));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtMistBlood, args.Target);
        _visualsSystem.Strike(entity, args.Target, FxColour.Blood);
        var now = _gameTiming.CurTime;
        var wormComponent = EnsureComp<GuWormComponent>(hostUid);
        wormComponent.Master = entity;
        wormComponent.ExpiresAt = now + args.Lifetime;
        wormComponent.NextSip = now + GuSipInterval;
        AddTaint(entity, 3f);

        _noticeSystem.Notify(hostUid, Loc.GetString("cultivation-gu-sting"), type: PopupType.SmallCaution);
        _noticeSystem.Notify(entity, Loc.GetString("cultivation-gu-planted", ("target", hostUid)));
        _adminLogger.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(entity):player} planted a gu worm in {ToPrettyString(hostUid):target}");
    }

    [SubscribeLocalEvent]
    private void OnStirGu(Entity<CultivatorComponent> entity, ref StirGuActionEvent args)
    {
        if (args.Handled)
            return;

        var hostEntities = new List<Entity<GuWormComponent>>();
        var wormQuery = EntityQueryEnumerator<GuWormComponent>();
        while (wormQuery.MoveNext(out var hostUid, out var wormComponent))
        {
            // A worm already on its way out (expired or expelled this tick) has nothing left to bite with.
            if (wormComponent.Master == entity.Owner && wormComponent.LifeStage <= ComponentLifeStage.Running)
                hostEntities.Add((hostUid, wormComponent));
        }

        if (hostEntities.Count == 0)
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-gu-none"));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtMistBlood, entity);
        foreach (var hostEntity in hostEntities)
            StirWorm(hostEntity);

        _noticeSystem.Notify(entity, Loc.GetString("cultivation-gu-stirred"));
    }

    /// <summary>
    ///     The master twists their qi and, far away, the worm bites. It dies doing it.
    /// </summary>
    private void StirWorm(Entity<GuWormComponent> host)
    {
        // The worm's bite grows with its master's realm.
        var power = _cultivationSystem.Power(host.Comp.Master);
        _stunSystem.TryUpdateParalyzeDuration(host, TimeSpan.FromSeconds(3));
        _damageableSystem.TryChangeDamage(host.Owner, CultivationSystem.Damage("Poison", 30f * power), ignoreResistances: true);
        _staminaSystem.TakeStaminaDamage(host, 60f);
        _vomitSystem.Vomit(host, force: true);
        _noticeSystem.Announce(host, Loc.GetString("cultivation-gu-chewing", ("target", host.Owner)), type: PopupType.LargeCaution);
        _adminLogger.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(host.Comp.Master):player} stirred the gu worm in {ToPrettyString(host.Owner):target}");
        RemComp<GuWormComponent>(host);
    }

    private bool HasGuBane(EntityUid hostUid)
    {
        if (!TryComp<BloodstreamComponent>(hostUid, out var bloodstreamComponent)
            || !_solutionContainerSystem.ResolveSolution(hostUid, bloodstreamComponent.BloodSolutionName,
                ref bloodstreamComponent.BloodSolution, out var bloodSolution))
            return false;

        foreach (var reagent in GuBane)
        {
            if (bloodSolution.GetTotalPrototypeQuantity(reagent) > 0)
                return true;
        }

        return false;
    }

    private void ExpelWorm(Entity<GuWormComponent> host)
    {
        _vomitSystem.Vomit(host, force: true);
        _noticeSystem.Announce(host, Loc.GetString("cultivation-gu-expelled", ("target", host.Owner)), type: PopupType.Medium);
        if (host.Comp.Master is { } masterUid)
            _noticeSystem.Notify(masterUid, Loc.GetString("cultivation-gu-died"), type: PopupType.SmallCaution);

        RemCompDeferred<GuWormComponent>(host);
    }

    #endregion

    #region Soul Search

    [SubscribeLocalEvent]
    private void OnSoulSearch(Entity<CultivatorComponent> entity, ref SoulSearchActionEvent args)
    {
        if (args.Handled)
            return;

        var victimUid = args.Target;
        if (_mobStateSystem.IsDead(victimUid) || !_mindSystem.TryGetMind(victimUid, out _, out _))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-soul-search-no-soul"));
            return;
        }

        if (!IsHelpless(victimUid))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-soul-search-not-helpless", ("target", victimUid)));
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, entity, args.Duration, new SoulSearchDoAfterEvent(), entity, target: victimUid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = 1.5f,
        };

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost) || !_doAfterSystem.TryStartDoAfter(doAfterArgs))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtVoidMotes, args.Target);
        _visualsSystem.Strike(entity, args.Target, FxColour.Blood);
        _jitteringSystem.DoJitter(victimUid, args.Duration, refresh: true);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-soul-search-start", ("user", entity.Owner), ("target", victimUid)), type: PopupType.MediumCaution);
        _noticeSystem.Notify(victimUid, Loc.GetString("cultivation-soul-search-felt"), type: PopupType.LargeCaution);
    }

    /// <summary>
    ///     Restrained, stunned, knocked down or in critical condition.
    /// </summary>
    private bool IsHelpless(EntityUid uid)
    {
        return _mobStateSystem.IsCritical(uid)
            || _cuffableSystem.IsCuffed(uid)
            || HasComp<StunnedComponent>(uid)
            || HasComp<KnockedDownComponent>(uid);
    }

    [SubscribeLocalEvent]
    private void OnSoulSearchDone(Entity<CultivatorComponent> entity, ref SoulSearchDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } victimUid
            || !_mindSystem.TryGetMind(victimUid, out var mindId, out var mindComponent))
            return;

        args.Handled = true;
        var reportBuilder = new StringBuilder();
        reportBuilder.AppendLine(Loc.GetString("cultivation-soul-search-header", ("name", Name(victimUid))));
        reportBuilder.AppendLine(Loc.GetString("cultivation-soul-search-calling", ("job", _jobSystem.MindTryGetJobName(mindId))));

        var allegiances = new List<string>();
        foreach (var roleInfo in _roleSystem.MindGetAllRoleInfo((mindId, mindComponent)))
        {
            if (roleInfo.Antagonist)
                allegiances.Add(Loc.GetString(roleInfo.Name));
        }

        reportBuilder.AppendLine(allegiances.Count > 0
            ? Loc.GetString("cultivation-soul-search-allegiance", ("allegiances", string.Join(", ", allegiances)))
            : Loc.GetString("cultivation-soul-search-no-allegiance"));

        if (_cultivatorQuery.TryComp(victimUid, out var victimCultivator))
        {
            var laws = new List<string>();
            foreach (var lawId in victimCultivator.Laws)
            {
                if (ProtoMan.TryIndex(lawId, out var lawPrototype))
                    laws.Add(Loc.GetString(lawPrototype.Name));
            }

            reportBuilder.AppendLine(Loc.GetString("cultivation-soul-search-cultivation",
                ("realm", Loc.GetString($"cultivation-realm-{victimCultivator.Realm}")),
                ("laws", laws.Count > 0 ? string.Join(", ", laws) : Loc.GetString("cultivation-soul-search-no-laws")),
                ("demonic", IsDemonic(victimUid) ? "true" : "false")));
        }

        if (TryComp<BodyCultivatorComponent>(victimUid, out var victimBody) && victimBody.Stage > 0)
            reportBuilder.AppendLine(Loc.GetString("cultivation-soul-search-body", ("stage", Loc.GetString($"body-stage-{victimBody.Stage}"))));

        SendPrivately(entity, reportBuilder.ToString().TrimEnd());

        // Being rummaged through leaves a mark.
        _damageableSystem.TryChangeDamage(victimUid, CultivationSystem.Damage("Cellular", 10f), ignoreResistances: true, origin: entity);
        _jitteringSystem.DoJitter(victimUid, TimeSpan.FromSeconds(15), refresh: true);
        _noticeSystem.Notify(victimUid, Loc.GetString("cultivation-soul-search-seen"), type: PopupType.MediumCaution);
        _sharedCultivationSystem.AddInsight(entity.AsNullable(), 10f);
        AddTaint(entity, 5f);
        _adminLogger.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(entity):player} soul searched {ToPrettyString(victimUid):target}");
    }

    private void SendPrivately(EntityUid uid, string message)
    {
        if (!TryComp<ActorComponent>(uid, out var actorComponent))
            return;

        _chatManager.ChatMessageToOne(ChatChannel.Server, message, message, EntityUid.Invalid, hideChat: false,
            actorComponent.PlayerSession.Channel, colorOverride: Color.FromHex("#d06a6a"));
    }

    #endregion

    #region Heavenly Demon Blood Escape

    [SubscribeLocalEvent]
    private void OnBloodEscape(Entity<CultivatorComponent> entity, ref BloodEscapeActionEvent args)
    {
        if (args.Handled)
            return;

        if (FindEscape(entity, args.MinDistance, args.MaxDistance) is not { } destinationCoordinates)
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-blood-escape-nowhere"));
            return;
        }

        if (!_cultivationSystem.TryUseTechnique(entity, args.QiCost))
            return;

        args.Handled = true;
        _visualsSystem.Art(CultivationVisualsSystem.ArtMistBlood, entity);
        _noticeSystem.Announce(entity, Loc.GetString("cultivation-blood-escape", ("user", entity.Owner)), type: PopupType.LargeCaution);

        // A quarter of the way to critical condition, spat out as blood.
        var lifeCost = 25f;
        if (_mobThresholdSystem.TryGetThresholdForState(entity, MobState.Critical, out var critThreshold))
            lifeCost = (float)critThreshold.Value * args.LifeFraction;
        _damageableSystem.TryChangeDamage(entity.Owner, CultivationSystem.Damage("Blunt", lifeCost), ignoreResistances: true, interruptsDoAfters: false);

        // No grip and no cuff can hold blood.
        if (TryComp<PullableComponent>(entity, out var pullableComponent) && pullableComponent.BeingPulled)
            _pullingSystem.TryStopPull(entity, pullableComponent);

        if (TryComp<CuffableComponent>(entity, out var cuffableComponent))
        {
            foreach (var cuffUid in cuffableComponent.Container.ContainedEntities.ToArray())
                _cuffableSystem.Uncuff(entity, null, cuffUid, cuffable: cuffableComponent);
        }

        _transformSystem.SetCoordinates(entity, destinationCoordinates);
        _audioSystem.PlayPvs(CurseSound, entity);
        AddTaint(entity, 4f);
    }

    /// <summary>
    ///     An open floor tile somewhere between the given distances, in any direction.
    /// </summary>
    private EntityCoordinates? FindEscape(EntityUid uid, float minDistance, float maxDistance)
    {
        var originCoordinates = Transform(uid).Coordinates;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var offsetVector = _robustRandom.NextAngle().ToVec() * _robustRandom.NextFloat(minDistance, maxDistance);
            var candidateCoordinates = originCoordinates.Offset(offsetVector);
            if (!_turfSystem.TryGetTileRef(candidateCoordinates, out var tileRef) || _turfSystem.IsSpace(tileRef.Value)
                || _turfSystem.IsTileBlocked(tileRef.Value, CollisionGroup.Impassable))
                continue;

            return _turfSystem.GetTileCenter(tileRef.Value);
        }

        return null;
    }

    #endregion
}
