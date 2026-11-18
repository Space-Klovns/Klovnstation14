using Content.Server._KS14.Cultivation.Components;
using Content.Server.Chat.Systems;
using Content.Shared._KS14.Cultivation.Jianghu;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Cultivation.Systems;

// The Martial World Ranking, decided only by honor duels, and the martial tournaments a Sect Master can proclaim.
// Both are round state, kept on the system and cleared with the round.
public sealed partial class JianghuSystem
{
    public const int RankingSize = 10;

    /// <summary>
    ///     First Under Heaven won't answer challenges from anyone ranked below this.
    /// </summary>
    private const int ChallengeFirstRank = 5;

    /// <summary>
    ///     The top three meditate this much more insightfully.
    /// </summary>
    private const float RankingInsightBonus = 0.1f;

    private static readonly TimeSpan RankingPairCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TournamentDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TournamentCooldown = TimeSpan.FromMinutes(30);

    [Dependency] private ChatSystem _chatSystem = default!;

    /// <summary>
    ///     Ranked minds, best first.
    /// </summary>
    private readonly List<EntityUid> _ranking = new();

    /// <summary>
    ///     The same two fighters can only move each other once every few minutes, so friends can't farm swaps.
    /// </summary>
    private readonly Dictionary<(EntityUid, EntityUid), TimeSpan> _rankingPairCooldowns = new();

    private EntityUid? _tournamentHost;
    private TimeSpan _tournamentEndsAt;
    private TimeSpan _tournamentCooldownUntil;
    private readonly Dictionary<EntityUid, int> _tournamentWins = new();

    /// <summary>
    ///     The last tournament's champion, a mind.
    /// </summary>
    public EntityUid? Champion { get; private set; }

    public bool TournamentRunning => _tournamentHost != null;

    [SubscribeLocalEvent]
    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _ranking.Clear();
        _rankingPairCooldowns.Clear();
        _tournamentHost = null;
        _tournamentWins.Clear();
        _tournamentCooldownUntil = TimeSpan.Zero;
        Champion = null;
    }

    #region Ranking

    /// <summary>
    ///     1 for the best, 0 for the unranked.
    /// </summary>
    public int RankOf(EntityUid bodyUid)
    {
        return _mindSystem.TryGetMind(bodyUid, out var mindId, out _) ? RankOfMind(mindId) : 0;
    }

    private int RankOfMind(EntityUid mindId)
    {
        PruneRanking();
        return _ranking.IndexOf(mindId) + 1;
    }

    /// <summary>
    ///     A mind that no longer exists holds no place, and no champion's crown.
    /// </summary>
    private void PruneRanking()
    {
        _ranking.RemoveAll(mindId => !Exists(mindId));
        if (Champion is { } championMind && !Exists(championMind))
            Champion = null;

        if (_tournamentHost is { } hostUid && !Exists(hostUid))
            _tournamentHost = null;
    }

    /// <summary>
    ///     Whoever the jianghu calls First Under Heaven, a mind.
    /// </summary>
    public EntityUid? FirstUnderHeaven()
    {
        PruneRanking();
        return _ranking.Count > 0 ? _ranking[0] : null;
    }

    private float RankingInsightBonusFor(EntityUid bodyUid)
    {
        var rank = RankOf(bodyUid);
        return rank is > 0 and <= 3 ? RankingInsightBonus : 0f;
    }

    /// <summary>
    ///     Win against someone ranked above you and take their place; beat someone unranked and join the list.
    /// </summary>
    private void RecordRanking(EntityUid winnerUid, EntityUid loserUid)
    {
        if (!_mindSystem.TryGetMind(winnerUid, out var winnerMind, out _) || !_mindSystem.TryGetMind(loserUid, out var loserMind, out _)
            || winnerMind == loserMind)
            return;

        var pairKey = winnerMind.Id < loserMind.Id ? (winnerMind, loserMind) : (loserMind, winnerMind);
        if (_rankingPairCooldowns.TryGetValue(pairKey, out var pairCooldown) && _gameTiming.CurTime < pairCooldown)
        {
            _noticeSystem.Notify(winnerUid, Loc.GetString("jianghu-ranking-weighed"));
            return;
        }

        var oldFirst = FirstUnderHeaven();
        var winnerRank = RankOfMind(winnerMind);
        var loserRank = RankOfMind(loserMind);

        if (loserRank > 0 && (winnerRank == 0 || winnerRank > loserRank))
        {
            // Take the loser's place; everyone between slides down one.
            if (winnerRank > 0)
                _ranking.RemoveAt(winnerRank - 1);
            _ranking.Insert(loserRank - 1, winnerMind);
        }
        else if (winnerRank == 0 && _ranking.Count < RankingSize)
        {
            _ranking.Add(winnerMind);
        }
        else
        {
            return;
        }

        if (_ranking.Count > RankingSize)
            _ranking.RemoveRange(RankingSize, _ranking.Count - RankingSize);

        _rankingPairCooldowns[pairKey] = _gameTiming.CurTime + RankingPairCooldown;
        EnsureComp<JianghuExamineComponent>(winnerUid);
        _noticeSystem.Notify(winnerUid, Loc.GetString("jianghu-ranking-climbed", ("rank", RankOfMind(winnerMind))), type: PopupType.Large);
        if (loserRank > 0)
            _noticeSystem.Notify(loserUid, Loc.GetString("jianghu-ranking-fell", ("rank", RankOfMind(loserMind))), type: PopupType.MediumCaution);

        if (FirstUnderHeaven() == winnerMind && oldFirst != winnerMind)
        {
            _noticeSystem.Proclaim(null, Loc.GetString("jianghu-first-under-heaven", ("name", Name(winnerUid))),
                Loc.GetString("jianghu-ranking-sender"), colorOverride: SectColor);
        }
    }

    /// <summary>
    ///     Someone takes the top of the ranking outright (the Dragon Slaying Saber does that), and the station hears.
    /// </summary>
    public void ClaimFirstUnderHeaven(EntityUid bodyUid, string announcementLocId)
    {
        if (!_mindSystem.TryGetMind(bodyUid, out var mindId, out _))
            return;

        PruneRanking();
        if (FirstUnderHeaven() == mindId)
            return;

        _ranking.Remove(mindId);
        _ranking.Insert(0, mindId);
        if (_ranking.Count > RankingSize)
            _ranking.RemoveRange(RankingSize, _ranking.Count - RankingSize);

        EnsureComp<JianghuExamineComponent>(bodyUid);
        _noticeSystem.Proclaim(null, Loc.GetString(announcementLocId, ("name", Name(bodyUid))),
            Loc.GetString("jianghu-ranking-sender"), colorOverride: SectColor);
    }

    /// <summary>
    ///     First Under Heaven is beneath challenges from anyone outside the top five.
    /// </summary>
    public bool RefusesChallenge(EntityUid challengerUid, EntityUid opponentUid)
    {
        if (!_mindSystem.TryGetMind(opponentUid, out var opponentMind, out _) || FirstUnderHeaven() != opponentMind)
            return false;

        var challengerRank = RankOf(challengerUid);
        return challengerRank == 0 || challengerRank > ChallengeFirstRank;
    }

    /// <summary>
    ///     One line per ranked fighter, best first.
    /// </summary>
    public List<string> RankingLines()
    {
        var lines = new List<string> { Loc.GetString("jianghu-ranking-header") };
        if (_ranking.Count == 0)
        {
            lines.Add(Loc.GetString("jianghu-ranking-empty"));
            return lines;
        }

        for (var index = 0; index < _ranking.Count; index++)
        {
            var name = TryComp<MindComponent>(_ranking[index], out var mindComponent) && mindComponent.CharacterName is { } characterName
                ? characterName
                : Loc.GetString("jianghu-ranking-unknown");
            lines.Add(Loc.GetString("jianghu-ranking-line", ("rank", index + 1), ("name", FormattedMessage.EscapeText(name))));
        }

        return lines;
    }

    #endregion

    #region Tournaments

    /// <summary>
    ///     For ten minutes every honor duel wins double face, and whoever wins the most is crowned Martial Champion.
    /// </summary>
    public bool TryProclaimTournament(Entity<SectComponent> host, EntityUid proclaimerUid)
    {
        if (TournamentRunning)
        {
            _noticeSystem.Notify(proclaimerUid, Loc.GetString("jianghu-tournament-running"));
            return false;
        }

        if (_gameTiming.CurTime < _tournamentCooldownUntil)
        {
            _noticeSystem.Notify(proclaimerUid, Loc.GetString("jianghu-tournament-cooldown",
                ("minutes", (int)Math.Ceiling((_tournamentCooldownUntil - _gameTiming.CurTime).TotalMinutes))));
            return false;
        }

        _tournamentHost = host;
        _tournamentEndsAt = _gameTiming.CurTime + TournamentDuration;
        _tournamentCooldownUntil = _gameTiming.CurTime + TournamentCooldown;
        _tournamentWins.Clear();
        _noticeSystem.Proclaim(null, Loc.GetString("jianghu-tournament-proclaimed", ("sect", FormattedMessage.EscapeText(host.Comp.SectName))),
            Loc.GetString("jianghu-tournament-sender"), colorOverride: SectColor);
        return true;
    }

    private void RecordTournamentWin(EntityUid winnerUid)
    {
        if (!TournamentRunning || !_mindSystem.TryGetMind(winnerUid, out var winnerMind, out _))
            return;

        var wins = _tournamentWins.GetValueOrDefault(winnerMind) + 1;
        _tournamentWins[winnerMind] = wins;
        _noticeSystem.Announce(winnerUid, Loc.GetString("jianghu-tournament-wins", ("user", winnerUid), ("wins", wins)));
    }

    public void ConcludeTournament()
    {
        _tournamentHost = null;
        EntityUid? championMind = null;
        var championWins = 0;
        foreach (var (mindId, wins) in _tournamentWins)
        {
            if (wins > championWins)
                (championMind, championWins) = (mindId, wins);
        }

        _tournamentWins.Clear();
        if (championMind is not { } champion || !TryComp<MindComponent>(champion, out var mindComponent))
        {
            _noticeSystem.Proclaim(null, Loc.GetString("jianghu-tournament-empty"), Loc.GetString("jianghu-tournament-sender"),
                colorOverride: SectColor);
            return;
        }

        Champion = champion;
        _noticeSystem.Proclaim(null, Loc.GetString("jianghu-tournament-champion",
                ("name", FormattedMessage.EscapeText(mindComponent.CharacterName ?? string.Empty)), ("wins", championWins)),
            Loc.GetString("jianghu-tournament-sender"), colorOverride: SectColor);

        if (mindComponent.OwnedEntity is not { } championUid)
            return;

        EnsureComp<JianghuExamineComponent>(championUid);
        AdjustFace(championUid, 10, "jianghu-reason-champion");
        _sharedCultivationSystem.AddInsight(championUid, 30f);
    }

    private void UpdateTournament(TimeSpan now)
    {
        if (TournamentRunning && now >= _tournamentEndsAt)
            ConcludeTournament();
    }

    #endregion

    #region Duel results

    /// <summary>
    ///     Beating a sworn rival is worth more, and so is any victory during a tournament.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnDuelResolving(ref HonorDuelResolvingEvent args)
    {
        if (AreRivals(args.Winner, args.Loser) && GetSect(args.Winner) is { } winnerSect && GetSect(args.Loser) is { } loserSect)
        {
            args.FaceGain += 5;
            AnnounceToSect(winnerSect, Loc.GetString("jianghu-rival-defeated", ("winner", Name(args.Winner)), ("loser", Name(args.Loser)),
                ("rival", FormattedMessage.EscapeText(loserSect.Comp.SectName))));
            AnnounceToSect(loserSect, Loc.GetString("jianghu-rival-lost", ("winner", Name(args.Winner)), ("loser", Name(args.Loser)),
                ("rival", FormattedMessage.EscapeText(winnerSect.Comp.SectName))));
        }

        if (TournamentRunning)
            args.FaceGain *= 2;
    }

    [SubscribeLocalEvent]
    private void OnDuelFinished(ref HonorDuelFinishedEvent args)
    {
        if (args.Winner is not { } winnerUid || args.Loser is not { } loserUid)
            return;

        SectMissionProgress(winnerUid, SectMission.Duel, 1);
        RecordTournamentWin(winnerUid);
        RecordRanking(winnerUid, loserUid);
    }

    #endregion
}
