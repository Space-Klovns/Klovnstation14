using Content.Shared.Verbs;
using System.Linq;
using Content.Server.Administration;
using Content.Server.Chat.Systems;
using Content.Server._KS14.Cultivation.Body;
using Content.Server._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Stunnable;
using Content.Shared.Damage.Components;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The Mandate of Heaven, ported from SurfShack13. In a wuxia round the heads of staff rule their departments with
///         heaven's blessing and the Captain is the Son of Heaven. A ruler carries Dragon Qi (the weak who strike them
///         are rebuked), Heaven's Protection (less damage, and one fatal blow turned aside), Heaven's Fortune (a blow now
///         and then simply misses), better breakthroughs and faster cultivation, and grows stronger the longer the
///         dynasty lasts. They proclaim Imperial Decrees, command the weak to kneel, open the Imperial Treasury, pardon
///         the disgraced, appoint Jinyiwei and accept the fealty of sects; the Son of Heaven also ennobles the crew and
///         calls down Heaven's Punishment.
///     Heaven withdraws the Mandate when a ruler dies, is demoted or is kept in chains: a jade seal falls where they
///         stood, and whoever raises it to heaven claims the Mandate. Murder a ruler and heaven marks you a regicide.
/// </summary>
public sealed partial class MandateSystem : EntitySystem
{
    private static readonly TimeSpan RestraintLimit = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DemotionLimit = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan[] DynastyTiers = { TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60) };
    private const double AscensionDynastySpeed = 2.0;
    private static readonly TimeSpan RegicideWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RegicideDuration = TimeSpan.FromMinutes(5);
    private static readonly EntProtoId JadeSealPrototype = "KsJadeSeal";
    private static readonly EntProtoId CourtAction = "ActionKsImperialCourt";

    private static readonly MandatePower[] RulerPowers = { MandatePower.Decree, MandatePower.Kneel, MandatePower.Treasury, MandatePower.Pardon, MandatePower.Jinyiwei, MandatePower.Fealty };
    private static readonly MandatePower[] SonPowers = { MandatePower.Ennoble, MandatePower.Punishment };

    private static readonly Dictionary<MandatePower, TimeSpan> Cooldowns = new()
    {
        { MandatePower.Decree, TimeSpan.FromSeconds(90) },
        { MandatePower.Kneel, TimeSpan.FromSeconds(60) },
        { MandatePower.Treasury, TimeSpan.FromSeconds(10) },
        { MandatePower.Pardon, TimeSpan.FromMinutes(5) },
        { MandatePower.Jinyiwei, TimeSpan.FromSeconds(30) },
        { MandatePower.Fealty, TimeSpan.FromSeconds(60) },
        { MandatePower.Ennoble, TimeSpan.FromSeconds(30) },
        { MandatePower.Punishment, TimeSpan.FromSeconds(30) },
    };

    /// <summary>
    ///     How far away someone can be and still be named in the court.
    /// </summary>
    private const float CourtRange = 9f;
    private static readonly LocId[] NobleTitles = { "mandate-title-duke", "mandate-title-marquis", "mandate-title-count", "mandate-title-viscount", "mandate-title-baron" };

    /// <summary>
    ///     The heads of staff and the department each rules; the Captain (no department) is the Son of Heaven.
    /// </summary>
    private static readonly Dictionary<string, string?> Heads = new()
    {
        { "Captain", null },
        { "ChiefEngineer", "Engineering" },
        { "ChiefMedicalOfficer", "Medical" },
        { "HeadOfPersonnel", "Civilian" },
        { "HeadOfSecurity", "Security" },
        { "ResearchDirector", "Science" },
        { "Quartermaster", "Cargo" },
    };

    /// <summary>
    ///     Shock damage from each of Heaven's Punishment's three bolts.
    /// </summary>
    private const int PunishmentShock = 25;

    [Dependency] private WuxiaAscensionRuleSystem _ascensionSystem = default!;
    [Dependency] private Content.Server.Electrocution.ElectrocutionSystem _electrocutionSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private CultivationLootSystem _lootSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private CultivationStanceSystem _stanceSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private JianghuSystem _jianghuSystem = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;
    [Dependency] private QuickDialogSystem _quickDialogSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private SharedJobSystem _jobSystem = default!;
    [Dependency] private SharedMindSystem _mindSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private WuxiaWorldRuleSystem _worldRuleSystem = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    #region Granting and losing

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId != null && _worldRuleSystem.WuxiaActive())
            GrantForJob(args.Mob, args.JobId);
    }

    /// <summary>
    ///     When the jianghu wakes mid-round, the heads already aboard receive their Mandates.
    /// </summary>
    /// <summary>
    ///     Whether this job is a head of staff (one heaven grants a Mandate to).
    /// </summary>
    public static bool IsHeadJob(string jobId)
    {
        return Heads.ContainsKey(jobId);
    }

    public void GrantExistingHeads()
    {
        var bodies = EntityQueryEnumerator<MindContainerComponent>();
        while (bodies.MoveNext(out var uid, out var container))
        {
            if (container.Mind is { } mindId && _jobSystem.MindTryGetJobId(mindId, out var jobId) && jobId is { } job)
                GrantForJob(uid, job.Id);
        }
    }

    private void GrantForJob(EntityUid uid, string jobId)
    {
        if (!Heads.TryGetValue(jobId, out var domain) || HasComp<MandateOfHeavenComponent>(uid) || !_mobStateSystem.IsAlive(uid))
            return;

        Grant(uid, domain, son: domain == null, job: jobId);
    }

    /// <summary>
    ///     Heaven grants someone its Mandate.
    /// </summary>
    public Entity<MandateOfHeavenComponent>? Grant(EntityUid uid, string? domain, bool son, string? job = null, List<string>? lineage = null)
    {
        if (HasComp<MandateOfHeavenComponent>(uid))
            return null;

        var mandate = AddComp<MandateOfHeavenComponent>(uid);
        mandate.Domain = domain;
        mandate.SonOfHeaven = son;
        mandate.GrantedJob = job;
        mandate.HeldSince = _gameTiming.CurTime;
        if (lineage != null)
            mandate.Lineage = new List<string>(lineage);

        if (son)
            EnsureComp<SonOfHeavenComponent>(uid);

        // One button for every power: the Imperial Court.
        mandate.CourtAction = _actionsSystem.AddAction(uid, CourtAction);
        var ui = EnsureComp<UserInterfaceComponent>(uid);
        if (!_uiSystem.HasUi(uid, ImperialCourtUiKey.Key, ui))
            _uiSystem.SetUi((uid, ui), ImperialCourtUiKey.Key, new InterfaceData("ImperialCourtBoundUserInterface"));

        _visualsSystem.Wave(uid, FxColour.Gold, big: true);
        _noticeSystem.Notify(uid, Loc.GetString(son ? "mandate-granted-son" : "mandate-granted", ("domain", DomainName(mandate))), PopupType.Large, alsoToChat: true);
        return (uid, mandate);
    }

    public string DomainName(MandateOfHeavenComponent mandate)
    {
        return DomainName(mandate.Domain, mandate.SonOfHeaven);
    }

    private string DomainName(string? domain, bool son)
    {
        if (son || domain == null || !ProtoMan.TryIndex<DepartmentPrototype>(domain, out var department))
            return Loc.GetString("mandate-domain-station");

        return Loc.GetString(department.Name);
    }

    public string TitleOf(MandateOfHeavenComponent mandate)
    {
        return mandate.SonOfHeaven
            ? Loc.GetString("mandate-title-son")
            : Loc.GetString("mandate-title-lord", ("domain", DomainName(mandate)));
    }

    /// <summary>
    ///     Heaven withdraws its favour: a jade seal falls where the ruler stood, and an omen tells the station.
    /// </summary>
    public void Withdraw(Entity<MandateOfHeavenComponent> entity, LocId reason)
    {
        var mandate = entity.Comp;
        var sealUid = Spawn(JadeSealPrototype, _transformSystem.GetMapCoordinates(entity));
        var seal = EnsureComp<JadeSealComponent>(sealUid);
        seal.Domain = mandate.Domain;
        seal.SonOfHeaven = mandate.SonOfHeaven;
        seal.Lineage = new List<string>(mandate.Lineage) { Name(entity) };
        _metaDataSystem.SetEntityName(sealUid, mandate.SonOfHeaven
            ? Loc.GetString("mandate-seal-name-son")
            : Loc.GetString("mandate-seal-name", ("domain", DomainName(mandate))));

        var because = Loc.GetString(reason);
        _noticeSystem.Notify(entity, Loc.GetString("mandate-withdrawn-self"), PopupType.LargeCaution, alsoToChat: true);
        if (mandate.SonOfHeaven)
        {
            _noticeSystem.Proclaim(entity, Loc.GetString("mandate-withdrawn-son", ("ruler", Name(entity)), ("reason", because)),
                Loc.GetString("mandate-omen-sender"), announcementSound: CultivationVisualsSystem.ThunderclapSound, colorOverride: Color.Gold);
        }
        else
        {
            _noticeSystem.Proclaim(entity, Loc.GetString("mandate-withdrawn", ("ruler", Name(entity)), ("domain", DomainName(mandate)),
                ("reason", because)), Loc.GetString("mandate-omen-sender"), playDefaultSound: false, colorOverride: Color.Gold);
        }

        _visualsSystem.HeavenBolt(sealUid);
        RemComp<MandateOfHeavenComponent>(entity);
    }

    [SubscribeLocalEvent]
    private void OnMandateRemoved(Entity<MandateOfHeavenComponent> entity, ref ComponentRemove args)
    {
        RemComp<SonOfHeavenComponent>(entity);
        _actionsSystem.RemoveAction(entity.Owner, entity.Comp.CourtAction);
        QueueDel(entity.Comp.CourtAction);
        _uiSystem.CloseUi(entity.Owner, ImperialCourtUiKey.Key);

        // Titles and offices die with the throne.
        foreach (var appointee in entity.Comp.Nobles.Concat(entity.Comp.Guards))
        {
            if (TerminatingOrDeleted(appointee))
                continue;

            RemComp<ImperialNobleComponent>(appointee);
            RemComp<ImperialGuardComponent>(appointee);
            _ascensionSystem.RefreshRole(appointee);
            _noticeSystem.Notify(appointee, Loc.GetString("mandate-office-falls"), PopupType.MediumCaution);
        }

        _jianghuSystem.EndFealty(entity);
    }

    [SubscribeLocalEvent]
    private void OnRulerMobState(Entity<MandateOfHeavenComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        // Whoever struck the last blow is marked a regicide.
        if (entity.Comp.LastAttacker is { } killer && !TerminatingOrDeleted(killer)
            && _gameTiming.CurTime - entity.Comp.LastAttacked <= RegicideWindow)
        {
            EnsureComp<RegicideCurseComponent>(killer).Until = _gameTiming.CurTime + RegicideDuration;
            _visualsSystem.Wave(killer, FxColour.Blood, big: true);
            _noticeSystem.Notify(killer, Loc.GetString("mandate-regicide"), PopupType.LargeCaution, alsoToChat: true);
        }

        Withdraw(entity, "mandate-reason-died");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _gameTiming.CurTime;

        var punished = EntityQueryEnumerator<HeavensPunishmentComponent>();
        while (punished.MoveNext(out var uid, out var punishment))
        {
            if (now < punishment.NextStrike)
                continue;

            punishment.NextStrike = now + TimeSpan.FromSeconds(1.2);
            _visualsSystem.HeavenBolt(uid);
            // A real electrocution: the jolt, the shaking and the stun. No glove or insulation stops heaven; if the
            //     shock can't take hold at all, the bolt still burns them.
            if (!_electrocutionSystem.TryDoElectrocution(uid, null, PunishmentShock, TimeSpan.FromSeconds(3), refresh: true, ignoreInsulation: true))
                _damageableSystem.TryChangeDamage(uid, CultivationSystem.Damage("Shock", PunishmentShock), ignoreResistances: true);
            _stunSystem.TryKnockdown(uid, TimeSpan.FromSeconds(2));
            if (--punishment.StrikesLeft <= 0)
                RemCompDeferred<HeavensPunishmentComponent>(uid);
        }

        if (now < _nextCheck)
            return;

        var step = TimeSpan.FromSeconds(1);
        _nextCheck = now + step;
        var rulers = EntityQueryEnumerator<MandateOfHeavenComponent>();
        var withdrawn = new List<(Entity<MandateOfHeavenComponent>, LocId)>();
        while (rulers.MoveNext(out var uid, out var mandate))
        {
            UpdateDynasty((uid, mandate));

            // Kept in chains.
            if (TryComp<CuffableComponent>(uid, out var cuffable) && cuffable.CuffedHandCount > 0)
            {
                mandate.RestrainedTime += step;
                if (mandate.RestrainedTime >= RestraintLimit)
                {
                    withdrawn.Add(((uid, mandate), "mandate-reason-chains"));
                    continue;
                }
            }
            else
            {
                mandate.RestrainedTime = mandate.RestrainedTime > step ? mandate.RestrainedTime - step : TimeSpan.Zero;
            }

            // Stripped of their rank: only a Mandate that came with a job cares about paperwork.
            if (mandate.GrantedJob is { } job && ProtoMan.TryIndex(job, out var jobPrototype)
                && _idCardSystem.TryFindIdCard(uid, out var idCard)
                && idCard.Comp.LocalizedJobTitle != jobPrototype.LocalizedName)
            {
                mandate.DemotedTime += step;
                if (mandate.DemotedTime >= DemotionLimit)
                    withdrawn.Add(((uid, mandate), "mandate-reason-demoted"));
            }
            else
            {
                mandate.DemotedTime = TimeSpan.Zero;
            }
        }

        foreach (var (ruler, reason) in withdrawn)
            Withdraw(ruler, reason);

        var cursed = EntityQueryEnumerator<RegicideCurseComponent>();
        while (cursed.MoveNext(out var uid, out var curse))
        {
            if (now >= curse.Until)
                RemCompDeferred<RegicideCurseComponent>(uid);
        }
    }

    private void UpdateDynasty(Entity<MandateOfHeavenComponent> entity)
    {
        var held = _gameTiming.CurTime - entity.Comp.HeldSince;
        // Ascension is a race: the dynasty rises (and the treasury refills) twice as fast.
        if (_ascensionSystem.Active())
            held *= AscensionDynastySpeed;
        var tier = DynastyTiers.Count(threshold => held >= threshold);
        if (tier <= entity.Comp.DynastyTier)
            return;

        entity.Comp.DynastyTier = tier;
        entity.Comp.TreasuryCharges++;
        if (tier >= 3)
            entity.Comp.PunishmentCharges++;

        _visualsSystem.Wave(entity, FxColour.Gold, big: true);
        _visualsSystem.Play(CultivationVisualsSystem.TempleBellSound, entity);
        _noticeSystem.Notify(entity, Loc.GetString("mandate-dynasty-endures", ("tier", tier)), PopupType.Large, alsoToChat: true);
    }

    #endregion

    #region The seal

    [SubscribeLocalEvent]
    private void OnSealExamined(Entity<JadeSealComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("mandate-seal-examine", ("domain", DomainName(entity.Comp.Domain, entity.Comp.SonOfHeaven))));
        if (entity.Comp.Lineage.Count > 0)
            args.PushMarkup(Loc.GetString("mandate-seal-lineage", ("lineage", string.Join(", ", entity.Comp.Lineage))));
    }

    [SubscribeLocalEvent]
    private void OnSealUsed(Entity<JadeSealComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var user = args.User;
        if (HasComp<MandateOfHeavenComponent>(user))
        {
            _noticeSystem.Notify(user, Loc.GetString("mandate-seal-already"), PopupType.SmallCaution);
            return;
        }

        if (HasComp<RegicideCurseComponent>(user))
        {
            _noticeSystem.Notify(user, Loc.GetString("mandate-seal-regicide"), PopupType.SmallCaution);
            return;
        }

        _noticeSystem.Announce(user, Loc.GetString("mandate-seal-raising", ("user", user), ("seal", entity.Owner)), type: PopupType.Medium);
        _visualsSystem.Play(CultivationVisualsSystem.TempleBellSound, user);
        _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, user, entity.Comp.ClaimTime, new JadeSealClaimDoAfterEvent(), entity, used: entity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnSealClaimed(Entity<JadeSealComponent> entity, ref JadeSealClaimDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || TerminatingOrDeleted(entity) || HasComp<MandateOfHeavenComponent>(args.User))
            return;

        args.Handled = true;
        Claim(args.User, entity);
    }

    /// <summary>
    ///     Someone raises a seal to heaven and claims its Mandate.
    /// </summary>
    public bool Claim(EntityUid user, Entity<JadeSealComponent> entity)
    {
        if (HasComp<RegicideCurseComponent>(user)
            || Grant(user, entity.Comp.Domain, entity.Comp.SonOfHeaven, job: null, lineage: entity.Comp.Lineage) is not { } mandate)
            return false;

        _visualsSystem.Breakthrough(user);
        _noticeSystem.Proclaim(user, entity.Comp.SonOfHeaven
                ? Loc.GetString("mandate-claimed-son", ("ruler", Name(user)))
                : Loc.GetString("mandate-claimed", ("ruler", Name(user)), ("domain", DomainName(mandate.Comp))),
            Loc.GetString("mandate-omen-sender"), playDefaultSound: false, colorOverride: Color.Gold);
        QueueDel(entity);
        return true;
    }

    #endregion

    #region Being a ruler

    [SubscribeLocalEvent]
    private void OnRulerExamined(Entity<MandateOfHeavenComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("mandate-examine", ("ruler", entity.Owner), ("title", TitleOf(entity.Comp)),
            ("seized", entity.Comp.GrantedJob == null ? "yes" : "no")));
    }

    [SubscribeLocalEvent]
    private void OnNobleExamined(Entity<ImperialNobleComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("mandate-noble-examine", ("noble", entity.Owner), ("title", Loc.GetString(entity.Comp.Title))));
    }

    [SubscribeLocalEvent]
    private void OnRegicideExamined(Entity<RegicideCurseComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("mandate-regicide-examine", ("user", entity.Owner)));
    }

    /// <summary>
    ///     How much less damage Heaven's Protection lets through.
    /// </summary>
    public float ProtectionReduction(MandateOfHeavenComponent mandate, EntityUid uid)
    {
        return (mandate.SonOfHeaven ? 0.2f : 0.1f) + 0.025f * mandate.DynastyTier + 0.02f * Math.Min(_jianghuSystem.SectsSwornTo(uid), 3);
    }

    /// <summary>
    ///     Heaven's Protection softens every blow; Heaven's Fortune makes some miss entirely.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRulerDamageModify(Entity<MandateOfHeavenComponent> entity, ref DamageModifyEvent args)
    {
        if (args.Damage.GetTotal() <= 0)
            return;

        if (args.Origin is { } origin && origin != entity.Owner)
        {
            entity.Comp.LastAttacker = origin;
            entity.Comp.LastAttacked = _gameTiming.CurTime;
            if (_robustRandom.Prob((entity.Comp.SonOfHeaven ? 0.1f : 0.05f) + 0.02f * entity.Comp.DynastyTier))
            {
                args.Damage *= 0f;
                _visualsSystem.Spark(entity, FxColour.Gold);
                _noticeSystem.Announce(entity, Loc.GetString("mandate-fortune", ("ruler", entity.Owner)), type: PopupType.Small);
                return;
            }
        }

        args.Damage *= 1f - ProtectionReduction(entity.Comp, entity);
    }

    /// <summary>
    ///     A blow that would fell the ruler is turned aside, and heaven answers the attacker with lightning.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRulerDamaged(Entity<MandateOfHeavenComponent> entity, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || _gameTiming.CurTime < entity.Comp.ProtectionReady || _mobStateSystem.IsDead(entity)
            || !_mobThresholdSystem.TryGetThresholdForState(entity, MobState.Critical, out var critical)
            || _damageableSystem.GetTotalDamage(entity.Owner) < critical)
            return;

        entity.Comp.ProtectionReady = _gameTiming.CurTime + (entity.Comp.SonOfHeaven ? TimeSpan.FromMinutes(6) : TimeSpan.FromMinutes(10));
        var overflow = _damageableSystem.GetTotalDamage(entity.Owner) - critical.Value + 15;
        _damageableSystem.HealEvenly(entity.Owner, -overflow);
        _visualsSystem.Wave(entity, FxColour.Gold, big: true);
        _visualsSystem.Boom(CultivationVisualsSystem.GreatBellSound, _transformSystem.GetMapCoordinates(entity), volume: 4f, range: 20f);
        _noticeSystem.Announce(entity, Loc.GetString("mandate-protection", ("ruler", entity.Owner)), type: PopupType.LargeCaution);
        if (entity.Comp.LastAttacker is { } attacker && !TerminatingOrDeleted(attacker)
            && _gameTiming.CurTime - entity.Comp.LastAttacked <= TimeSpan.FromSeconds(5)
            && _transformSystem.InRange((attacker, null), (entity.Owner, null), 12f))
        {
            _visualsSystem.HeavenBolt(attacker);
            _damageableSystem.TryChangeDamage(attacker, CultivationSystem.Damage("Shock", 25f), ignoreResistances: true);
            _noticeSystem.Notify(attacker, Loc.GetString("mandate-heaven-angry"), PopupType.LargeCaution);
        }
    }

    /// <summary>
    ///     Dragon Qi: the weak who strike a ruler are rebuked, and the ruler's Jinyiwei are told.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRulerAttacked(Entity<MandateOfHeavenComponent> entity, ref AttackedEvent args)
    {
        var assailant = args.User;
        if (assailant == entity.Owner)
            return;

        foreach (var guardUid in entity.Comp.Guards)
        {
            if (!TerminatingOrDeleted(guardUid) && guardUid != assailant && _mobStateSystem.IsAlive(guardUid))
                _noticeSystem.Notify(guardUid, Loc.GetString("mandate-guard-alert", ("lord", entity.Owner), ("assailant", assailant),
                    ("where", _noticeSystem.Where(entity))), PopupType.LargeCaution, alsoToChat: true);
        }

        var now = _gameTiming.CurTime;
        if (HasComp<MandateOfHeavenComponent>(assailant)
            || entity.Comp.RebukeCooldowns.TryGetValue(assailant, out var ready) && now < ready
            || PathPower(assailant) > PathPower(entity) + (entity.Comp.SonOfHeaven ? 2 : 0))
            return;

        entity.Comp.RebukeCooldowns[assailant] = now + TimeSpan.FromSeconds(10);
        _stanceSystem.ApplySnare(assailant, TimeSpan.FromSeconds(3), 0.6f);
        _visualsSystem.Spark(assailant, FxColour.Gold);
        _noticeSystem.Notify(assailant, Loc.GetString("mandate-dragon-qi"), PopupType.MediumCaution);
        if (entity.Comp.SonOfHeaven || entity.Comp.DynastyTier >= 2 || _jianghuSystem.SectsSwornTo(entity) >= 2)
            _stunSystem.TryKnockdown(assailant, TimeSpan.FromSeconds(0.5 + 0.25 * entity.Comp.DynastyTier));
    }

    /// <summary>
    ///     Someone's standing on either path, for comparing who heaven protects: a body stage, or a qi realm counted as
    ///         two stages a realm.
    /// </summary>
    private int PathPower(EntityUid uid)
    {
        if (TryComp<BodyCultivatorComponent>(uid, out var body))
            return body.Stage;

        return TryComp<CultivatorComponent>(uid, out var cultivator) ? 1 + 2 * (int) cultivator.Realm : 0;
    }

    [SubscribeLocalEvent]
    private void OnRulerReadiness(Entity<MandateOfHeavenComponent> entity, ref CultivationBreakthroughReadinessEvent args)
    {
        args.Readiness += ReadinessBonus(entity.Comp);
    }

    [SubscribeLocalEvent]
    private void OnRulerTribulationReadiness(Entity<MandateOfHeavenComponent> entity, ref BodyTribulationReadinessEvent args)
    {
        args.Readiness += ReadinessBonus(entity.Comp);
    }

    private static int ReadinessBonus(MandateOfHeavenComponent mandate)
    {
        return (mandate.SonOfHeaven ? 15 : 10) + 5 * mandate.DynastyTier;
    }

    #endregion

    #region Powers

    public IEnumerable<MandatePower> PowersOf(MandateOfHeavenComponent mandate)
    {
        return mandate.SonOfHeaven ? RulerPowers.Concat(SonPowers) : RulerPowers;
    }

    [SubscribeLocalEvent]
    private void OnCourtAction(Entity<MandateOfHeavenComponent> entity, ref ImperialCourtActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        UpdateCourt(entity);
        _uiSystem.CloseUi(entity.Owner, ImperialCourtUiKey.Key, args.Performer);
        _uiSystem.OpenUi(entity.Owner, ImperialCourtUiKey.Key, args.Performer);
    }

    private void UpdateCourt(Entity<MandateOfHeavenComponent> entity)
    {
        var people = Around(entity, CourtRange).Where(uid => HasComp<MindContainerComponent>(uid)).Select(uid => GetNetEntity(uid)).ToList();
        _uiSystem.SetUiState(entity.Owner, ImperialCourtUiKey.Key,
            new ImperialCourtState(PowersOf(entity.Comp).ToList(), new Dictionary<MandatePower, TimeSpan>(entity.Comp.ReadyAt), people));
    }

    [SubscribeLocalEvent]
    private void OnCourtPower(Entity<MandateOfHeavenComponent> entity, ref ImperialCourtPowerMessage args)
    {
        Use(entity, args.Power, GetEntity(args.Target));
        UpdateCourt(entity);
    }

    /// <summary>
    ///     A ruler uses one of their powers (on someone, for a targeted one). Fails if it isn't theirs, is still cooling
    ///         down, or the one named isn't close by.
    /// </summary>
    public bool Use(Entity<MandateOfHeavenComponent> entity, MandatePower power, EntityUid? target)
    {
        if (!PowersOf(entity.Comp).Contains(power) || !_mobStateSystem.IsAlive(entity))
            return false;

        var now = _gameTiming.CurTime;
        if (entity.Comp.ReadyAt.TryGetValue(power, out var ready) && now < ready)
        {
            _noticeSystem.Notify(entity, Loc.GetString("mandate-power-cooling", ("seconds", (int) Math.Ceiling((ready - now).TotalSeconds))), PopupType.SmallCaution);
            return false;
        }

        var subject = target ?? entity.Owner;
        if (MandatePowers.Targeted(power)
            && (target is not { } named || named == entity.Owner || TerminatingOrDeleted(named)
                || !_transformSystem.InRange((named, null), (entity.Owner, null), CourtRange)))
            return false;

        entity.Comp.ReadyAt[power] = now + Cooldowns[power];
        switch (power)
        {
            case MandatePower.Decree: CourtDecree(entity, subject); break;
            case MandatePower.Kneel: CourtKneel(entity, subject); break;
            case MandatePower.Treasury: CourtTreasury(entity, subject); break;
            case MandatePower.Pardon: CourtPardon(entity, subject); break;
            case MandatePower.Jinyiwei: CourtJinyiwei(entity, subject); break;
            case MandatePower.Fealty: CourtFealty(entity, subject); break;
            case MandatePower.Ennoble: CourtEnnoble(entity, subject); break;
            case MandatePower.Punishment: CourtPunishment(entity, subject); break;
        }

        return true;
    }

    /// <summary>
    ///     Living people around a ruler.
    /// </summary>
    private List<EntityUid> Around(EntityUid uid, float range)
    {
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(uid).Coordinates, range, _mobs);
        return _mobs.Where(mob => mob.Owner != uid && _mobStateSystem.IsAlive(mob.Owner)).Select(mob => mob.Owner).ToList();
    }

    private void Proclaim(EntityUid uid, string words)
    {
        _chatSystem.TrySendInGameICMessage(uid, words, InGameICChatType.Speak, hideChat: false, ignoreActionBlocker: true);
    }

    private bool InDomain(MandateOfHeavenComponent mandate, EntityUid subject)
    {
        if (mandate.Domain is not { } domain)
            return true;

        return _mindSystem.TryGetMind(subject, out var mindId, out _)
               && _jobSystem.MindTryGetJobId(mindId, out var job) && job is { } jobId
               && ProtoMan.TryIndex<DepartmentPrototype>(domain, out var department)
               && department.Roles.Contains(jobId);
    }

    private void CourtDecree(Entity<MandateOfHeavenComponent> entity, EntityUid target)
    {
        if (!TryComp<ActorComponent>(entity, out var actor))
            return;

        var ruler = entity.Owner;
        _quickDialogSystem.OpenDialog<string>(actor.PlayerSession, Loc.GetString("mandate-decree-title"), Loc.GetString("mandate-decree-prompt"),
            text =>
            {
                if (TerminatingOrDeleted(ruler) || !TryComp<MandateOfHeavenComponent>(ruler, out var mandate))
                    return;

                text = Robust.Shared.Utility.FormattedMessage.RemoveMarkupPermissive(text).Trim();
                if (text.Length == 0)
                    return;

                if (text.Length > 200)
                    text = text[..200];

                Decree((ruler, mandate), text);
            });
    }

    private void Decree(Entity<MandateOfHeavenComponent> entity, string text)
    {
        _visualsSystem.Wave(entity, FxColour.Gold, big: true);
        _visualsSystem.Play(CultivationVisualsSystem.GreatBellSound, entity);
        if (entity.Comp.SonOfHeaven)
        {
            _noticeSystem.Proclaim(entity, text, Loc.GetString("mandate-decree-sender-son", ("ruler", Name(entity))),
                announcementSound: CultivationVisualsSystem.GreatBellSound, colorOverride: Color.Gold);
        }
        else
        {
            Proclaim(entity, Loc.GetString("mandate-decree-words", ("text", text)));
        }

        // Their people nearby are rallied: faster, and their strength restored.
        foreach (var subjectUid in Around(entity, 9f).Prepend(entity.Owner))
        {
            if (subjectUid != entity.Owner && !InDomain(entity.Comp, subjectUid))
                continue;

            _stanceSystem.ApplyHaste(subjectUid, TimeSpan.FromSeconds(20), 1.25f);
            _staminaSystem.TakeStaminaDamage(subjectUid, -30f);
            _visualsSystem.Wave(subjectUid, FxColour.Gold);
            _noticeSystem.Notify(subjectUid, Loc.GetString("mandate-rallied"), PopupType.Medium);
        }
    }

    private void CourtKneel(Entity<MandateOfHeavenComponent> entity, EntityUid target)
    {
        Proclaim(entity, Loc.GetString("mandate-kneel-shout"));
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtRoarGold, entity);
        var reach = (entity.Comp.SonOfHeaven ? 6f : 4f) + entity.Comp.DynastyTier;
        var power = PathPower(entity);
        foreach (var subjectUid in Around(entity, reach))
        {
            if (HasComp<MandateOfHeavenComponent>(subjectUid) || PathPower(subjectUid) > power)
                continue;

            _stunSystem.TryKnockdown(subjectUid, TimeSpan.FromSeconds(entity.Comp.SonOfHeaven ? 3 : 2));
            _noticeSystem.Notify(subjectUid, Loc.GetString("mandate-kneel-forced"), PopupType.MediumCaution);
        }
    }

    private void CourtTreasury(Entity<MandateOfHeavenComponent> entity, EntityUid target)
    {
        if (entity.Comp.TreasuryCharges <= 0)
        {
            _noticeSystem.Notify(entity, Loc.GetString("mandate-treasury-empty"), PopupType.SmallCaution);
            return;
        }

        entity.Comp.TreasuryCharges--;
        var here = _transformSystem.GetMapCoordinates(entity);
        var tier = entity.Comp.DynastyTier;

        // A young dynasty's treasury is modest; an old one is a hoard. Spirit herbs, talismans and riches every time,
        // then a manual, then martial arts, the jianghu's treasured armour and, more and more surely, a legendary.
        for (var i = 0; i < (entity.Comp.SonOfHeaven ? 2 : 1) + tier / 2; i++)
            Spawn("KsSpiritHerb", here);
        for (var i = 0; i < 3 + tier; i++)
            Spawn("KsQiTalisman", here);
        for (var i = 0; i <= tier; i++)
        {
            Spawn("IngotGold", here);
            Spawn("MaterialDiamond1", here);
        }

        Spawn(_robustRandom.Prob(0.5f) ? _robustRandom.Pick(CultivationLootSystem.QiManuals) : CultivationLootSystem.BodyMoldingArt, here);
        if (tier >= 1)
            Spawn(_robustRandom.Pick(CultivationLootSystem.MartialManuals), here);
        if (tier >= 2)
            Spawn(_robustRandom.Pick(CultivationLootSystem.LegendaryArmours), here);
        if (tier >= 3)
            Spawn(_robustRandom.Pick(CultivationLootSystem.MartialManuals), here);

        var artifactChance = entity.Comp.SonOfHeaven
            ? (entity.Comp.TreasuryArtifactGiven ? 0.4f + 0.2f * tier : tier >= 1 ? 1f : 0.4f)
            : 0.1f + 0.25f * tier;
        // Only a legendary not already in the round: once every one is out, the treasury has none left to give.
        if (_robustRandom.Prob(Math.Min(1f, artifactChance)) && _lootSystem.SpawnLegendaryAt(here))
            entity.Comp.TreasuryArtifactGiven = true;

        _visualsSystem.Wave(entity, FxColour.Gold, big: true);
        _visualsSystem.Play(CultivationVisualsSystem.WindChimesSound, entity);
        _noticeSystem.Announce(entity, Loc.GetString("mandate-treasury-opened", ("ruler", entity.Owner)), type: PopupType.Medium);
    }

    private void CourtPardon(Entity<MandateOfHeavenComponent> entity, EntityUid target)
    {
        if (target == entity.Owner)
            return;

        _jianghuSystem.Pardon(target);
        Proclaim(entity, Loc.GetString("mandate-pardon-words", ("subject", Name(target))));
        _visualsSystem.Wave(target, FxColour.Gold);
    }

    private void CourtJinyiwei(Entity<MandateOfHeavenComponent> entity, EntityUid target)
    {
        if (target == entity.Owner)
            return;

        var appointee = target;
        if (TryComp<ImperialGuardComponent>(appointee, out var existing) && existing.Lord == entity.Owner)
        {
            RemComp<ImperialGuardComponent>(appointee);
            entity.Comp.Guards.Remove(appointee);
            _ascensionSystem.RefreshRole(appointee);
            Proclaim(entity, Loc.GetString("mandate-jinyiwei-dismissed", ("subject", Name(appointee))));
            return;
        }

        entity.Comp.Guards.RemoveWhere(uid => TerminatingOrDeleted(uid) || !HasComp<ImperialGuardComponent>(uid));
        if (entity.Comp.Guards.Count >= (entity.Comp.SonOfHeaven ? 2 : 1))
        {
            _noticeSystem.Notify(entity, Loc.GetString("mandate-jinyiwei-full"), PopupType.SmallCaution);
            return;
        }

        var ruler = entity.Owner;
        // Joining is always the appointee's choice. In Ascension it costs them the race, and the question says so.
        var question = Loc.GetString("mandate-jinyiwei-ask", ("ruler", Name(ruler)));
        if (_ascensionSystem.Active())
            question += " " + Loc.GetString("mandate-jinyiwei-ask-ascension");

        Ask(appointee, Loc.GetString("mandate-jinyiwei-title"), question, () =>
        {
            if (TerminatingOrDeleted(ruler) || !TryComp<MandateOfHeavenComponent>(ruler, out var mandate))
                return;

            EnsureComp<ImperialGuardComponent>(appointee).Lord = ruler;
            mandate.Guards.Add(appointee);
            _ascensionSystem.RefreshRole(appointee);
            Proclaim(ruler, Loc.GetString("mandate-jinyiwei-appointed", ("subject", Name(appointee))));
        });
    }

    private void CourtFealty(Entity<MandateOfHeavenComponent> entity, EntityUid target)
    {
        if (target == entity.Owner)
            return;

        var master = target;
        if (_jianghuSystem.GetSect(master) is not { } sect || !_jianghuSystem.IsSectMaster(master))
        {
            _noticeSystem.Notify(entity, Loc.GetString("mandate-fealty-not-master"), PopupType.SmallCaution);
            return;
        }

        var ruler = entity.Owner;
        Ask(master, Loc.GetString("mandate-fealty-title"), Loc.GetString("mandate-fealty-ask", ("ruler", Name(ruler))), () =>
        {
            if (!TerminatingOrDeleted(ruler) && HasComp<MandateOfHeavenComponent>(ruler))
                _jianghuSystem.SwearFealty(sect, ruler);
        });
    }

    private void CourtEnnoble(Entity<MandateOfHeavenComponent> entity, EntityUid target)
    {
        if (target == entity.Owner || !entity.Comp.SonOfHeaven)
            return;

        var subject = target;
        if (TryComp<ImperialNobleComponent>(subject, out var existing))
        {
            Proclaim(entity, Loc.GetString("mandate-ennoble-stripped", ("subject", Name(subject)), ("title", Loc.GetString(existing.Title))));
            RemComp<ImperialNobleComponent>(subject);
            entity.Comp.Nobles.Remove(subject);
            return;
        }

        entity.Comp.Nobles.RemoveWhere(uid => TerminatingOrDeleted(uid) || !HasComp<ImperialNobleComponent>(uid));
        if (entity.Comp.Nobles.Count >= 3 + entity.Comp.DynastyTier)
        {
            _noticeSystem.Notify(entity, Loc.GetString("mandate-ennoble-full"), PopupType.SmallCaution);
            return;
        }

        // The first raised are the highest: a duke, then a marquis, a count, a viscount, then barons.
        var title = NobleTitles[Math.Min(entity.Comp.Nobles.Count, NobleTitles.Length - 1)];
        var noble = EnsureComp<ImperialNobleComponent>(subject);
        noble.Title = title;
        noble.Lord = entity.Owner;
        entity.Comp.Nobles.Add(subject);
        _visualsSystem.Wave(subject, FxColour.Sense, big: true);
        Proclaim(entity, Loc.GetString("mandate-ennoble-words", ("subject", Name(subject)), ("title", Loc.GetString(title))));
    }

    private void CourtPunishment(Entity<MandateOfHeavenComponent> entity, EntityUid target)
    {
        if (target == entity.Owner || !entity.Comp.SonOfHeaven)
            return;

        if (entity.Comp.PunishmentCharges <= 0)
        {
            _noticeSystem.Notify(entity, Loc.GetString("mandate-punishment-spent"), PopupType.SmallCaution);
            return;
        }

        entity.Comp.PunishmentCharges--;
        Proclaim(entity, Loc.GetString("mandate-punishment-shout", ("condemned", Name(target).ToUpperInvariant())));
        _noticeSystem.Proclaim(entity, Loc.GetString("mandate-punishment-omen", ("condemned", Name(target))),
            Loc.GetString("mandate-omen-sender"), playDefaultSound: false, colorOverride: Color.Gold);
        var punishment = EnsureComp<HeavensPunishmentComponent>(target);
        punishment.StrikesLeft = 3;
        punishment.NextStrike = _gameTiming.CurTime;
    }

    /// <summary>
    ///     Ask someone to accept (an office, an oath). OK accepts, Cancel refuses.
    /// </summary>
    private void Ask(EntityUid uid, string title, string question, Action accepted)
    {
        if (!TryComp<ActorComponent>(uid, out var actor))
            return;

        _quickDialogSystem.OpenDialog<string>(actor.PlayerSession, title, question, _ => accepted());
    }

    #endregion

    /// <summary>
    ///     Heaven's favour on how fast someone cultivates: rulers, their nobles and Jinyiwei. (Sects sworn to a ruler
    ///         count in <see cref="JianghuSystem.CompanyBonus"/>.)
    /// </summary>
    public float CultivationBonus(EntityUid uid)
    {
        var bonus = 0f;
        if (TryComp<MandateOfHeavenComponent>(uid, out var mandate))
            bonus += mandate.SonOfHeaven ? 0.25f : 0.15f;
        if (HasComp<ImperialNobleComponent>(uid))
            bonus += 0.15f;
        if (HasComp<ImperialGuardComponent>(uid))
            bonus += 0.1f;
        return bonus;
    }
}
