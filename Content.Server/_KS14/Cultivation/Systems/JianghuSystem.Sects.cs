using Content.Shared._KS14.Cultivation.Body;
using Content.Server._KS14.Cultivation.Components;
using Content.Server.Administration;
using Content.Server.Chat.Managers;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Jianghu;
using Content.Shared._KS14.McqDialogue;
using Content.Shared.Administration.Logs;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Cultivation.Systems;

// Sects: founded at a plaque, joined with the master's blessing, bound by telepathic transmission, at odds with
// their rivals, and always working on a mission.
public sealed partial class JianghuSystem
{
    private static readonly EntProtoId PlaquePrototype = "KsSectPlaque";
    private static readonly EntProtoId TransmissionAction = "ActionJianghuSectTransmission";
    private static readonly EntProtoId RivalryAction = "ActionJianghuDeclareRivalry";

    private const string PetitionAcceptOption = "petition-accept";
    private const string PetitionRefuseOption = "petition-refuse";
    private const string TournamentOption = "tournament";
    private const string BowOption = "bow";
    private const string JianOption = "jian";
    private static readonly EntProtoId SectJianPrototype = "KsSectJian";

    private const int MaxSectNameLength = 32;
    private const float FellowshipRange = 5f;
    private const float FellowshipBonusPerMember = 0.1f;
    private const float MaxFellowshipBonus = 0.3f;
    private const float SwornTogetherBonus = 0.15f;
    private const float LiegeBonus = 0.1f;
    private const float PlaqueMeditationRange = 5f;

    private static readonly string[] SectWords = { "sect", "school", "palace", "pavilion", "hall", "gate", "manor" };
    private static readonly Color SectColor = Color.FromHex("#c9a227");

    private static readonly TimeSpan FirstMissionDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan NextMissionDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MissionDuration = TimeSpan.FromMinutes(20);

    /// <summary>
    ///     Each mission and how many of it a sect is asked for.
    /// </summary>
    private static readonly (SectMission Mission, int Min, int Max)[] MissionTable =
    {
        (SectMission.Pills, 2, 5),
        (SectMission.Duel, 1, 3),
        (SectMission.Meditate, 6, 12),
        (SectMission.Recruit, 1, 2),
        (SectMission.HeartDemon, 1, 1),
    };

    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private CultivationSystem _cultivationSystem = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private QuickDialogSystem _quickDialogSystem = default!;

    #region Membership

    /// <summary>
    ///     The sect entity whoever's mind is in this body belongs to.
    /// </summary>
    public Entity<SectComponent>? GetSect(EntityUid bodyUid)
    {
        if (!_mindSystem.TryGetMind(bodyUid, out var mindId, out _) || !TryComp<SectMemberComponent>(mindId, out var memberComponent)
            || !TryComp<SectComponent>(memberComponent.Sect, out var sectComponent))
            return null;

        return (memberComponent.Sect, sectComponent);
    }

    public bool IsSectMaster(EntityUid bodyUid)
    {
        return GetSect(bodyUid) is { } sect && _mindSystem.TryGetMind(bodyUid, out var mindId, out _) && sect.Comp.Master == mindId;
    }

    /// <summary>
    ///     Found a sect with this body's mind as master and a plaque where they stand.
    /// </summary>
    public Entity<SectComponent>? FoundSect(EntityUid founderUid, string sectName)
    {
        if (!_mindSystem.TryGetMind(founderUid, out var mindId, out _))
            return null;

        var sectUid = Spawn(null, MapCoordinates.Nullspace);
        var sectComponent = AddComp<SectComponent>(sectUid);
        sectComponent.SectName = sectName;
        sectComponent.Master = mindId;
        sectComponent.NextMission = _gameTiming.CurTime + FirstMissionDelay;
        _metaDataSystem.SetEntityName(sectUid, sectName);

        var plaqueUid = Spawn(PlaquePrototype, Transform(founderUid).Coordinates);
        EnsureComp<SectPlaqueComponent>(plaqueUid).Sect = sectUid;
        sectComponent.Plaque = plaqueUid;
        _metaDataSystem.SetEntityName(plaqueUid, Loc.GetString("jianghu-plaque-name", ("sect", sectName)));

        var sect = new Entity<SectComponent>(sectUid, sectComponent);
        AddMember(sect, founderUid);

        EntityUid? rivalryUid = null;
        if (_actionsSystem.AddAction(founderUid, ref rivalryUid, RivalryAction))
            _loadoutSystem.Learned(founderUid, rivalryUid.Value);

        _noticeSystem.Announce(founderUid, Loc.GetString("jianghu-sect-founded", ("user", founderUid), ("sect", sectName)), type: PopupType.Large);
        _visualsSystem.Wave(plaqueUid, FxColour.Gold, big: true);
        _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(founderUid):player} founded the sect {sectName}");
        return sect;
    }

    /// <summary>
    ///     Make a name fit for a sect: no markup, not too long, and a word that says what it is.
    /// </summary>
    public static string? CleanSectName(string rawName)
    {
        var sectName = FormattedMessage.RemoveMarkupPermissive(rawName).Trim();
        if (sectName.Length > MaxSectNameLength)
            sectName = sectName[..MaxSectNameLength].Trim();

        if (sectName.Length == 0)
            return null;

        foreach (var sectWord in SectWords)
        {
            if (sectName.Contains(sectWord, StringComparison.OrdinalIgnoreCase))
                return sectName;
        }

        return $"{sectName} Sect";
    }

    public void AddMember(Entity<SectComponent> sect, EntityUid bodyUid)
    {
        if (!_mindSystem.TryGetMind(bodyUid, out var mindId, out _) || sect.Comp.Members.Contains(mindId))
            return;

        if (GetSect(bodyUid) is { } oldSect)
            RemoveMember(oldSect, bodyUid);

        sect.Comp.Members.Add(mindId);
        var memberComponent = EnsureComp<SectMemberComponent>(mindId);
        memberComponent.Sect = sect;
        EnsureComp<JianghuExamineComponent>(bodyUid);

        EntityUid? transmissionUid = null;
        if (_actionsSystem.AddAction(bodyUid, ref transmissionUid, TransmissionAction))
        {
            memberComponent.TransmissionAction = transmissionUid;
            _loadoutSystem.Learned(bodyUid, transmissionUid.Value);
        }

        AnnounceToSect(sect, Loc.GetString("jianghu-sect-joined", ("name", Name(bodyUid)), ("rank", Loc.GetString(SectRank(sect, bodyUid)))));
        if (mindId != sect.Comp.Master)
            SectMissionProgress(bodyUid, SectMission.Recruit, 1);
    }

    public void RemoveMember(Entity<SectComponent> sect, EntityUid bodyUid)
    {
        if (!_mindSystem.TryGetMind(bodyUid, out var mindId, out _) || !sect.Comp.Members.Remove(mindId))
            return;

        if (TryComp<SectMemberComponent>(mindId, out var memberComponent))
            _loadoutSystem.Forget(bodyUid, memberComponent.TransmissionAction);

        RemComp<SectMemberComponent>(mindId);
    }

    /// <summary>
    ///     Sect Master, or an Elder, Inner or Outer Disciple by realm, as a locale id.
    /// </summary>
    public string SectRank(Entity<SectComponent> sect, EntityUid bodyUid)
    {
        if (_mindSystem.TryGetMind(bodyUid, out var mindId, out _) && mindId == sect.Comp.Master)
            return "jianghu-rank-master";

        if (!_cultivatorQuery.TryComp(bodyUid, out var cultivatorComponent))
            return "jianghu-rank-outer";

        return cultivatorComponent.Realm switch
        {
            >= CultivationRealm.GoldenCore => "jianghu-rank-elder",
            >= CultivationRealm.FoundationEstablishment => "jianghu-rank-inner",
            _ => "jianghu-rank-outer",
        };
    }

    /// <summary>
    ///     Every member in a body hears it.
    /// </summary>
    public void AnnounceToSect(Entity<SectComponent> sect, string message)
    {
        var wrappedMessage = Loc.GetString("jianghu-sect-wrap", ("sect", FormattedMessage.EscapeText(sect.Comp.SectName)), ("message", message));
        foreach (var mindId in sect.Comp.Members)
        {
            if (!TryComp<MindComponent>(mindId, out var mindComponent) || mindComponent.OwnedEntity is not { } bodyUid
                || !TryComp<ActorComponent>(bodyUid, out var actorComponent))
                continue;

            _chatManager.ChatMessageToOne(ChatChannel.Server, message, wrappedMessage, EntityUid.Invalid, hideChat: false,
                actorComponent.PlayerSession.Channel, colorOverride: SectColor);
        }
    }

    /// <summary>
    ///     The living bodies of a sect's members.
    /// </summary>
    private IEnumerable<EntityUid> MemberBodies(SectComponent sectComponent)
    {
        foreach (var mindId in sectComponent.Members)
        {
            if (TryComp<MindComponent>(mindId, out var mindComponent) && mindComponent.OwnedEntity is { } bodyUid
                && !TerminatingOrDeleted(bodyUid))
                yield return bodyUid;
        }
    }

    #endregion

    #region Actions

    [SubscribeLocalEvent]
    private void OnFoundSect(Entity<CultivatorComponent> entity, ref FoundSectActionEvent args)
    {
        if (args.Handled)
            return;

        if (IsSectMaster(entity))
        {
            _noticeSystem.Notify(entity, Loc.GetString("jianghu-sect-already-master"));
            return;
        }

        if (entity.Comp.Qi < args.QiCost || !TryComp<ActorComponent>(entity, out var actorComponent))
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-not-enough-qi"));
            return;
        }

        args.Handled = true;
        var founderUid = entity.Owner;
        var qiCost = args.QiCost;
        _quickDialogSystem.OpenDialog<string>(actorComponent.PlayerSession, Loc.GetString("jianghu-sect-name-title"),
            Loc.GetString("jianghu-sect-name-prompt"), rawName =>
            {
                if (TerminatingOrDeleted(founderUid) || IsSectMaster(founderUid) || CleanSectName(rawName) is not { } sectName
                    || !_cultivationSystem.TryUseTechnique(founderUid, qiCost))
                    return;

                FoundSect(founderUid, sectName);
            });
    }

    [SubscribeLocalEvent]
    private void OnSectTransmission(Entity<JianghuExamineComponent> entity, ref SectTransmissionActionEvent args)
    {
        if (args.Handled || GetSect(entity) is not { } sect || !TryComp<ActorComponent>(entity, out var actorComponent))
            return;

        args.Handled = true;
        var senderUid = entity.Owner;
        _quickDialogSystem.OpenDialog<string>(actorComponent.PlayerSession, Loc.GetString("jianghu-transmission-title"),
            Loc.GetString("jianghu-transmission-prompt"), rawMessage =>
            {
                var message = FormattedMessage.EscapeText(rawMessage.Trim());
                if (message.Length == 0 || TerminatingOrDeleted(senderUid) || GetSect(senderUid) is not { } currentSect)
                    return;

                AnnounceToSect(currentSect, Loc.GetString("jianghu-transmission", ("name", Name(senderUid)),
                    ("rank", Loc.GetString(SectRank(currentSect, senderUid))), ("message", message)));
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"{ToPrettyString(senderUid):player} sent a sect transmission to {currentSect.Comp.SectName}: {rawMessage}");
            });
    }

    [SubscribeLocalEvent]
    private void OnDeclareRivalry(Entity<JianghuExamineComponent> entity, ref DeclareRivalryActionEvent args)
    {
        if (args.Handled)
            return;

        if (!IsSectMaster(entity) || GetSect(entity) is not { } sect)
        {
            _noticeSystem.Notify(entity, Loc.GetString("jianghu-rivalry-not-master"));
            return;
        }

        // Point at a member of the other sect, or at its plaque.
        Entity<SectComponent>? rivalSect = GetSect(args.Target);
        if (rivalSect == null && TryComp<SectPlaqueComponent>(args.Target, out var plaqueComponent)
            && TryComp<SectComponent>(plaqueComponent.Sect, out var plaqueSectComponent))
            rivalSect = (plaqueComponent.Sect.Value, plaqueSectComponent);

        if (rivalSect is not { } rival || rival.Owner == sect.Owner || sect.Comp.Rivals.Contains(rival))
        {
            _noticeSystem.Notify(entity, Loc.GetString("jianghu-rivalry-no-target"));
            return;
        }

        args.Handled = true;
        DeclareRivalry(sect, rival);
    }

    public void DeclareRivalry(Entity<SectComponent> sect, Entity<SectComponent> rival)
    {
        sect.Comp.Rivals.Add(rival);
        rival.Comp.Rivals.Add(sect);
        AnnounceToSect(sect, Loc.GetString("jianghu-rivalry-declared", ("rival", FormattedMessage.EscapeText(rival.Comp.SectName))));
        AnnounceToSect(rival, Loc.GetString("jianghu-rivalry-received", ("rival", FormattedMessage.EscapeText(sect.Comp.SectName))));
    }

    public bool AreRivals(EntityUid oneUid, EntityUid twoUid)
    {
        return GetSect(oneUid) is { } oneSect && GetSect(twoUid) is { } twoSect && oneSect.Comp.Rivals.Contains(twoSect);
    }

    #endregion

    #region The plaque

    [SubscribeLocalEvent]
    private void OnPlaqueInteract(Entity<SectPlaqueComponent> entity, ref InteractHandEvent args)
    {
        if (args.Handled || !TryComp<SectComponent>(entity.Comp.Sect, out var sectComponent)
            || !_mindSystem.TryGetMind(args.User, out var userMindId, out _))
            return;

        args.Handled = true;
        var sect = new Entity<SectComponent>(entity.Comp.Sect.Value, sectComponent);

        if (userMindId == sectComponent.Master)
        {
            _mcqDialogueSystem.StartDialogue(entity, args.User, new McqDialogueData[]
            {
                new("jianghu-plaque-option-tournament", TournamentOption),
                new("jianghu-plaque-option-jian", JianOption),
                new("jianghu-plaque-option-bow", BowOption),
            });
            return;
        }

        if (sectComponent.Members.Contains(userMindId))
        {
            _noticeSystem.Notify(args.User, Loc.GetString("jianghu-plaque-bow", ("mission", MissionText(sectComponent))));
            return;
        }

        // Anyone else asks the master to take them in.
        if (!TryComp<MindComponent>(sectComponent.Master, out var masterMind) || masterMind.OwnedEntity is not { } masterUid
            || !HasComp<ActorComponent>(masterUid) || !_mobStateSystem.IsAlive(masterUid))
        {
            _noticeSystem.Notify(args.User, Loc.GetString("jianghu-petition-no-master"));
            return;
        }

        entity.Comp.Petitioner = args.User;
        _noticeSystem.Notify(args.User, Loc.GetString("jianghu-petition-sent", ("sect", sectComponent.SectName)));
        _noticeSystem.Notify(masterUid, Loc.GetString("jianghu-petition-received", ("user", args.User)));
        _mcqDialogueSystem.StartDialogue(entity, masterUid, new McqDialogueData[]
        {
            new("jianghu-petition-option-accept", PetitionAcceptOption),
            new("jianghu-petition-option-refuse", PetitionRefuseOption),
        });
    }

    [SubscribeLocalEvent]
    private void OnPlaqueDialogue(Entity<SectPlaqueComponent> entity, ref McqDialogueSelectedEvent args)
    {
        if (!TryComp<SectComponent>(entity.Comp.Sect, out var sectComponent)
            || !_mindSystem.TryGetMind(args.Actor, out var actorMindId, out _) || actorMindId != sectComponent.Master)
            return;

        var sect = new Entity<SectComponent>(entity.Comp.Sect.Value, sectComponent);
        switch (args.Id)
        {
            case PetitionAcceptOption when entity.Comp.Petitioner is { } petitionerUid && !TerminatingOrDeleted(petitionerUid):
                entity.Comp.Petitioner = null;
                AddMember(sect, petitionerUid);
                break;
            case PetitionRefuseOption when entity.Comp.Petitioner is { } petitionerUid:
                entity.Comp.Petitioner = null;
                _noticeSystem.Notify(petitionerUid, Loc.GetString("jianghu-petition-refused"));
                break;
            case TournamentOption:
                TryProclaimTournament(sect, args.Actor);
                break;
            case JianOption:
                TryIssueJian(sect, args.Actor);
                break;
        }
    }

    [SubscribeLocalEvent]
    private void OnPlaqueDestroyed(Entity<SectPlaqueComponent> entity, ref DestructionEventArgs args)
    {
        if (!TryComp<SectComponent>(entity.Comp.Sect, out var sectComponent))
            return;

        sectComponent.Plaque = null;
        AnnounceToSect((entity.Comp.Sect.Value, sectComponent), Loc.GetString("jianghu-plaque-destroyed"));
        foreach (var memberUid in MemberBodies(sectComponent))
            AdjustFace(memberUid, -2, "jianghu-reason-plaque");
    }

    [SubscribeLocalEvent]
    private void OnPlaqueExamined(Entity<SectPlaqueComponent> entity, ref ExaminedEvent args)
    {
        if (!TryComp<SectComponent>(entity.Comp.Sect, out var sectComponent))
            return;

        var masterName = TryComp<MindComponent>(sectComponent.Master, out var masterMind) && masterMind.CharacterName is { } characterName
            ? characterName
            : Loc.GetString("jianghu-plaque-no-master");
        args.PushMarkup(Loc.GetString("jianghu-plaque-examine", ("master", FormattedMessage.EscapeText(masterName)),
            ("members", sectComponent.Members.Count)));

        var rivalNames = new List<string>();
        foreach (var rivalUid in sectComponent.Rivals)
        {
            if (TryComp<SectComponent>(rivalUid, out var rivalComponent))
                rivalNames.Add(FormattedMessage.EscapeText(rivalComponent.SectName));
        }

        if (rivalNames.Count > 0)
            args.PushMarkup(Loc.GetString("jianghu-plaque-rivals", ("rivals", string.Join(", ", rivalNames))));

        args.PushMarkup(Loc.GetString("jianghu-plaque-mission", ("mission", MissionText(sectComponent)),
            ("completed", sectComponent.MissionsCompleted)));
        args.PushMarkup(Loc.GetString("jianghu-plaque-help"));
        foreach (var rankingLine in RankingLines())
            args.PushMarkup(rankingLine);
    }

    /// <summary>
    ///     The master takes a sword from the rack beneath the plaque: one for every member.
    /// </summary>
    public EntityUid? TryIssueJian(Entity<SectComponent> sect, EntityUid masterUid)
    {
        if (sect.Comp.JianIssued >= sect.Comp.Members.Count)
        {
            _noticeSystem.Notify(masterUid, Loc.GetString("jianghu-jian-all-issued"));
            return null;
        }

        sect.Comp.JianIssued++;
        var jianUid = Spawn(SectJianPrototype, Transform(masterUid).Coordinates);
        EnsureComp<SectJianComponent>(jianUid).Sect = sect;
        _metaDataSystem.SetEntityName(jianUid, Loc.GetString("jianghu-jian-name", ("sect", sect.Comp.SectName)));
        _handsSystem.PickupOrDrop(masterUid, jianUid, checkActionBlocker: false);
        _noticeSystem.Announce(masterUid, Loc.GetString("jianghu-jian-issued", ("user", masterUid)), type: PopupType.Medium);
        return jianUid;
    }

    #endregion

    #region Missions

    /// <summary>
    ///     Any member's work counts towards their sect's mission.
    /// </summary>
    public void SectMissionProgress(EntityUid bodyUid, SectMission mission, int amount)
    {
        if (GetSect(bodyUid) is not { } sect || sect.Comp.Mission != mission || _gameTiming.CurTime > sect.Comp.MissionDeadline)
            return;

        sect.Comp.MissionProgress = Math.Min(sect.Comp.MissionProgress + amount, sect.Comp.MissionGoal);
        if (sect.Comp.MissionProgress >= sect.Comp.MissionGoal)
        {
            CompleteMission(sect);
            return;
        }

        AnnounceToSect(sect, Loc.GetString("jianghu-mission-progress", ("progress", sect.Comp.MissionProgress), ("goal", sect.Comp.MissionGoal)));
    }

    public void IssueMission(Entity<SectComponent> sect, SectMission? mission = null, int? goal = null)
    {
        var (missionType, minGoal, maxGoal) = mission is { } chosen
            ? Array.Find(MissionTable, entry => entry.Mission == chosen)
            : _robustRandom.Pick(MissionTable);

        sect.Comp.Mission = missionType;
        sect.Comp.MissionGoal = goal ?? _robustRandom.Next(minGoal, maxGoal + 1);
        sect.Comp.MissionProgress = 0;
        sect.Comp.MissionDeadline = _gameTiming.CurTime + MissionDuration;
        AnnounceToSect(sect, Loc.GetString("jianghu-mission-new", ("mission", MissionText(sect.Comp))));
    }

    private void CompleteMission(Entity<SectComponent> sect)
    {
        sect.Comp.Mission = null;
        sect.Comp.MissionsCompleted++;
        sect.Comp.NextMission = _gameTiming.CurTime + NextMissionDelay;
        AnnounceToSect(sect, Loc.GetString("jianghu-mission-complete"));
        foreach (var memberUid in MemberBodies(sect.Comp))
        {
            if (!_mobStateSystem.IsAlive(memberUid))
                continue;

            _sharedCultivationSystem.AddInsight(memberUid, 15f);
            AdjustFace(memberUid, 3, "jianghu-reason-mission");
        }
    }

    private string MissionText(SectComponent sectComponent)
    {
        return sectComponent.Mission is { } mission
            ? Loc.GetString($"jianghu-mission-{mission}", ("goal", sectComponent.MissionGoal), ("progress", sectComponent.MissionProgress))
            : Loc.GetString("jianghu-mission-none");
    }

    private void UpdateSects(TimeSpan now)
    {
        var sectQuery = EntityQueryEnumerator<SectComponent>();
        while (sectQuery.MoveNext(out var sectUid, out var sectComponent))
        {
            if (sectComponent.Mission != null && now > sectComponent.MissionDeadline)
            {
                AnnounceToSect((sectUid, sectComponent), Loc.GetString("jianghu-mission-expired", ("mission", MissionText(sectComponent))));
                sectComponent.Mission = null;
                sectComponent.NextMission = now + NextMissionDelay;
            }
            else if (sectComponent.Mission == null && now >= sectComponent.NextMission && sectComponent.Members.Count > 0)
            {
                IssueMission((sectUid, sectComponent));
            }
        }
    }

    /// <summary>
    ///     Meditating beside the plaque counts for the sect, and so does company: fellow members close by make
    ///         meditation more insightful, as does a place near the top of the Martial World Ranking.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCultivatorMeditated(Entity<CultivatorComponent> entity, ref CultivationMeditatedEvent args)
    {
        if (GetSect(entity) is { } sect && sect.Comp.Plaque is { } plaqueUid
            && _transformSystem.InRange((entity.Owner, null), (plaqueUid, null), PlaqueMeditationRange))
            SectMissionProgress(entity, SectMission.Meditate, 1);
    }

    [SubscribeLocalEvent]
    private void OnInsightMultiplier(Entity<CultivatorComponent> entity, ref CultivationInsightMultiplierEvent args)
    {
        args.Multiplier += CompanyBonus(entity);
    }

    [SubscribeLocalEvent]
    private void OnTemperingMultiplier(Entity<BodyCultivatorComponent> entity, ref BodyTemperingMultiplierEvent args)
    {
        args.Multiplier += CompanyBonus(entity);
    }

    /// <summary>
    ///     How much faster someone cultivates for the company they keep: sect members close by, a sworn sibling at
    ///         their side and a place near the top of the Martial World Ranking. Counts for qi insight and body
    ///         tempering alike.
    /// </summary>
    public float CompanyBonus(EntityUid bodyUid)
    {
        return FellowshipBonus(bodyUid) + RankingInsightBonusFor(bodyUid)
               + (HasComp<SwornTogetherComponent>(bodyUid) ? SwornTogetherBonus : 0f)
               + (GetSect(bodyUid) is { Comp.Liege: { } liege } && HasComp<MandateOfHeavenComponent>(liege) ? LiegeBonus : 0f)
               + _mandateSystem.CultivationBonus(bodyUid)
               + _ascensionSystem.SpeedBonus();
    }

    /// <summary>
    ///     A sect master swears the sect's fealty to a ruler who holds the Mandate of Heaven.
    /// </summary>
    public void SwearFealty(Entity<SectComponent> sect, EntityUid rulerUid)
    {
        sect.Comp.Liege = rulerUid;
        AnnounceToSect(sect, Loc.GetString("mandate-fealty-sworn", ("ruler", Name(rulerUid))));
        _noticeSystem.Notify(rulerUid, Loc.GetString("mandate-fealty-accepted", ("sect", sect.Comp.SectName)), PopupType.Medium);
    }

    /// <summary>
    ///     A ruler lost the Mandate: every oath sworn to them ends.
    /// </summary>
    public void EndFealty(EntityUid rulerUid)
    {
        var sects = EntityQueryEnumerator<SectComponent>();
        while (sects.MoveNext(out var uid, out var sectComponent))
        {
            if (sectComponent.Liege != rulerUid)
                continue;

            sectComponent.Liege = null;
            AnnounceToSect((uid, sectComponent), Loc.GetString("mandate-fealty-ended"));
        }
    }

    /// <summary>
    ///     How many sects are sworn to a ruler.
    /// </summary>
    public int SectsSwornTo(EntityUid rulerUid)
    {
        var count = 0;
        var sects = EntityQueryEnumerator<SectComponent>();
        while (sects.MoveNext(out _, out var sectComponent))
        {
            if (sectComponent.Liege == rulerUid)
                count++;
        }

        return count;
    }

    private float FellowshipBonus(EntityUid bodyUid)
    {
        if (GetSect(bodyUid) is not { } sect)
            return 0f;

        var fellows = 0;
        foreach (var memberUid in MemberBodies(sect.Comp))
        {
            if (memberUid != bodyUid && _mobStateSystem.IsAlive(memberUid)
                && _transformSystem.InRange((memberUid, null), (bodyUid, null), FellowshipRange))
                fellows++;
        }

        return Math.Min((float)fellows * FellowshipBonusPerMember, MaxFellowshipBonus);
    }

    #endregion
}
