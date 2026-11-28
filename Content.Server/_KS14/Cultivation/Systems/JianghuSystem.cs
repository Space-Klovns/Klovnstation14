using Content.Shared.GameTicking;
using Content.Server._KS14.Cultivation.Components;
using Content.Shared.Examine;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The jianghu, the martial world: face (reputation), honor duels and what hangs off them. Face lives on the mind,
///         so it survives a change of body, and shows when someone is examined.
/// </summary>
public sealed partial class JianghuSystem : EntitySystem
{
    [Dependency] private WuxiaWorldRuleSystem _wuxiaWorldSystem = default!;
    [Dependency] private WuxiaAscensionRuleSystem _ascensionSystem = default!;
    [Dependency] private MandateSystem _mandateSystem = default!;
    [Dependency] private CultivationLoadoutSystem _loadoutSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedMindSystem _mindSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        UpdateChallenges(now);
        UpdateDuels(now);
        UpdateSects(now);
        UpdateTournament(now);
        UpdateBonds(now);
        UpdateIncense(now);
    }

    #region Face

    /// <summary>
    ///     The face of whoever's mind is in this body; nobody has any until they earn it.
    /// </summary>
    public int GetFace(EntityUid bodyUid)
    {
        return TryFaceComponent(bodyUid, out var faceComponent) ? faceComponent.Face : 0;
    }

    /// <summary>
    ///     Everyone aboard in a wuxia round has a standing in the jianghu, so everyone can examine themselves for it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        if (_wuxiaWorldSystem.WuxiaActive())
            EnsureComp<JianghuExamineComponent>(args.Mob);
    }

    public bool IsDishonored(EntityUid bodyUid)
    {
        return TryFaceComponent(bodyUid, out var faceComponent) && _gameTiming.CurTime < faceComponent.DishonoredUntil;
    }

    private bool TryFaceComponent(EntityUid bodyUid, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JianghuFaceComponent? faceComponent)
    {
        faceComponent = null;
        return _mindSystem.TryGetMind(bodyUid, out var mindId, out _) && TryComp(mindId, out faceComponent);
    }

    /// <summary>
    ///     The mind's face record, made the first time they're noticed.
    /// </summary>
    private JianghuFaceComponent? EnsureFaceComponent(EntityUid bodyUid)
    {
        if (!_mindSystem.TryGetMind(bodyUid, out var mindId, out _))
            return null;

        // A body on its way out (a duelist who vanished) keeps no marker, but the mind still remembers.
        if (!TerminatingOrDeleted(bodyUid))
            EnsureComp<JianghuExamineComponent>(bodyUid);

        return EnsureComp<JianghuFaceComponent>(mindId);
    }

    /// <summary>
    ///     Gain or lose face, and hear about it.
    /// </summary>
    /// <param name="reason">A locale id saying why, shown with the new total.</param>
    public void AdjustFace(EntityUid bodyUid, int amount, string reason)
    {
        if (amount == 0 || EnsureFaceComponent(bodyUid) is not { } faceComponent)
            return;

        faceComponent.Face += amount;
        _noticeSystem.Notify(bodyUid, Loc.GetString(amount > 0 ? "jianghu-face-gained" : "jianghu-face-lost",
                ("reason", Loc.GetString(reason)), ("face", faceComponent.Face)), type: amount > 0 ? PopupType.Medium : PopupType.MediumCaution);
    }

    /// <summary>
    ///     An imperial pardon: disgrace wiped away, lost face restored, and a little more besides.
    /// </summary>
    public void Pardon(EntityUid bodyUid)
    {
        if (EnsureFaceComponent(bodyUid) is not { } faceComponent)
            return;

        faceComponent.DishonoredUntil = TimeSpan.Zero;
        if (faceComponent.Face < 0)
            AdjustFace(bodyUid, -faceComponent.Face, "jianghu-reason-pardoned");
        AdjustFace(bodyUid, 3, "jianghu-reason-pardoned");
    }

    private void Dishonor(EntityUid bodyUid, TimeSpan duration)
    {
        if (EnsureFaceComponent(bodyUid) is { } faceComponent)
            faceComponent.DishonoredUntil = _gameTiming.CurTime + duration;
    }

    /// <summary>
    ///     What the jianghu calls someone with this much face, as a locale id, or null for nobody in particular.
    /// </summary>
    public static string? FaceTitle(int face)
    {
        return face switch
        {
            <= -10 => "jianghu-title-disgraced",
            < 0 => "jianghu-title-shamed",
            >= 30 => "jianghu-title-legend",
            >= 15 => "jianghu-title-hero",
            >= 5 => "jianghu-title-rising",
            _ => null,
        };
    }

    [SubscribeLocalEvent]
    private void OnJianghuExamined(Entity<JianghuExamineComponent> entity, ref ExaminedEvent args)
    {
        // Looking yourself over: your exact face and rank, and the Martial World Ranking.
        if (args.Examiner == entity.Owner)
        {
            var face = GetFace(entity);
            var ownTitle = FaceTitle(face) is { } titleId ? Loc.GetString(titleId) : Loc.GetString("jianghu-ranking-no-title");
            args.PushMarkup(Loc.GetString("jianghu-ranking-self", ("face", face), ("title", ownTitle), ("rank", RankOf(entity))));
            foreach (var rankingLine in RankingLines())
                args.PushMarkup(rankingLine);
        }

        ExamineStanding(entity, ref args);
        if (!TryFaceComponent(entity, out var faceComponent))
            return;

        if (FaceTitle(faceComponent.Face) is { } title)
            args.PushMarkup(Loc.GetString("jianghu-examine-title", ("target", entity.Owner), ("title", Loc.GetString(title))));

        if (_gameTiming.CurTime < faceComponent.DishonoredUntil)
            args.PushMarkup(Loc.GetString("jianghu-examine-dishonored", ("target", entity.Owner)));

        if (_gameTiming.CurTime < faceComponent.OathbrokenUntil)
            args.PushMarkup(Loc.GetString("jianghu-examine-oathbreaker", ("target", entity.Owner)));
    }

    /// <summary>
    ///     Sect, ranking and champion: everything the jianghu knows about someone beyond their face.
    /// </summary>
    private void ExamineStanding(Entity<JianghuExamineComponent> entity, ref ExaminedEvent args)
    {
        if (GetSect(entity) is { } sect)
        {
            var relation = "none";
            if (GetSect(args.Examiner) is { } examinerSect)
                relation = examinerSect.Owner == sect.Owner ? "fellow" : examinerSect.Comp.Rivals.Contains(sect) ? "rival" : "none";

            args.PushMarkup(Loc.GetString("jianghu-examine-sect", ("target", entity.Owner),
                ("rank", Loc.GetString(SectRank(sect, entity))), ("sect", FormattedMessage.EscapeText(sect.Comp.SectName)),
                ("relation", relation)));
        }

        if (GetBond(entity) is { } bond && _mindSystem.TryGetMind(entity, out var examinedMind, out _))
        {
            var siblingNames = new List<string>();
            foreach (var siblingMind in bond.Comp.Members)
            {
                if (siblingMind != examinedMind && TryComp<Content.Shared.Mind.MindComponent>(siblingMind, out var siblingComponent)
                    && siblingComponent.CharacterName is { } siblingName)
                    siblingNames.Add(FormattedMessage.EscapeText(siblingName));
            }

            args.PushMarkup(Loc.GetString("jianghu-examine-sworn", ("target", entity.Owner), ("siblings", string.Join(", ", siblingNames))));
        }

        if (!_mindSystem.TryGetMind(entity, out var mindId, out _))
            return;

        if (Champion == mindId)
            args.PushMarkup(Loc.GetString("jianghu-examine-champion", ("target", entity.Owner)));

        var rank = RankOfMind(mindId);
        if (rank == 1)
            args.PushMarkup(Loc.GetString("jianghu-examine-first", ("target", entity.Owner)));
        else if (rank > 0)
            args.PushMarkup(Loc.GetString("jianghu-examine-ranked", ("target", entity.Owner), ("rank", rank)));
    }

    #endregion
}
