using Content.Server._KS14.NPC.Perception;
using Content.Shared.Access.Systems;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Voices;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Callsigns;

/// <summary>
///     Hands out callsigns (<see cref="NpcCallsignComponent"/>), and has a squadmate call out the one going down - critical,
///         dead outright, or gibbed: the nearest that saw it, a moment later, by its callsign, or plainly for one with none.
///         Once only until it is back on its feet: going critical and then dying, or being gibbed, is one man down. Only in a
///         voice that calls out men down (<see cref="NpcCallout.ManDown"/>); one nobody saw go down goes unsaid.
/// </summary>
public sealed partial class NpcCallsignSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private NpcVoiceSystem _npcVoiceSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcCallsignComponent> _callsignQuery = default!;
    [Dependency] private EntityQuery<NpcSquadComponent> _squadQuery = default!;

    /// <summary>
    ///     How far off a squadmate may be, in tiles, and still see one go down.
    /// </summary>
    public const float SeeDownRange = 12f;

    /// <summary>
    ///     How long after one goes down it is called out: a moment to take it in.
    /// </summary>
    public static readonly TimeSpan CalloutDelay = TimeSpan.FromSeconds(0.6);

    /// <summary>
    ///     How often those called out are looked at again, to forget the ones back on their feet, or gone.
    /// </summary>
    private static readonly TimeSpan ForgetInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Callouts to come: who says it, and the line, worked out as it went down: a gibbed one is gone by then.
    /// </summary>
    private readonly List<(EntityUid SpeakerUid, LocId? Line, TimeSpan At)> _pending = new();

    /// <summary>
    ///     Those called out down, until they are alive again.
    /// </summary>
    private readonly HashSet<EntityUid> _calledOut = new();

    /// <summary>
    ///     How often the gear of those whose ID card is still to be named is looked through for one.
    /// </summary>
    private static readonly TimeSpan CardCheckInterval = TimeSpan.FromSeconds(0.25);

    private readonly List<EntityUid> _forget = new();
    private TimeSpan _nextForget;
    private TimeSpan _nextCardCheck;

    /// <summary>
    ///     Those whose ID card is still to get their callsign, and until when to wait for one: the card comes with the gear,
    ///         which may only be put on after the callsign is handed out.
    /// </summary>
    private readonly List<(EntityUid Uid, string Name, TimeSpan Until)> _unnamedCards = new();

    /// <summary>
    ///     How long to wait for an ID card to turn up.
    /// </summary>
    private static readonly TimeSpan IdCardWait = TimeSpan.FromSeconds(2);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NpcCallsignComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NpcSquadMemberDownedEvent>(OnMemberDowned);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    /// <summary>
    ///     Nothing from the round before carries over: whoever it was about is gone.
    /// </summary>
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _pending.Clear();
        _calledOut.Clear();
        _unnamedCards.Clear();
        _forget.Clear();
        _nextForget = TimeSpan.Zero;
    }

    /// <summary>
    ///     The callsign <paramref name="uid"/> goes by, if it has one.
    /// </summary>
    public bool TryGetCallsign(EntityUid uid, out NpcCallsign callsign)
    {
        callsign = default!;

        if (!_callsignQuery.TryComp(uid, out var callsignComponent) ||
            callsignComponent.Index is not { } index ||
            !_prototypeManager.TryIndex(callsignComponent.Set, out var set) ||
            index >= set.Callsigns.Count)
            return false;

        callsign = set.Callsigns[index];
        return true;
    }

    private void OnMapInit(Entity<NpcCallsignComponent> entity, ref MapInitEvent args)
    {
        if (!_prototypeManager.TryIndex(entity.Comp.Set, out var set))
            return;

        var mapId = Transform(entity).MapID;
        var taken = 0UL;
        var enumerator = EntityQueryEnumerator<NpcCallsignComponent, TransformComponent>();

        while (enumerator.MoveNext(out var otherUid, out var otherComponent, out var otherTransform))
        {
            if (otherUid != entity.Owner &&
                otherComponent.Set == entity.Comp.Set &&
                otherComponent.Index is { } otherIndex and < 64 &&
                otherTransform.MapID == mapId &&
                !_mobStateSystem.IsDead(otherUid))
                taken |= 1UL << otherIndex;
        }

        for (var i = 0; i < Math.Min(set.Callsigns.Count, 64); i++)
        {
            if ((taken & (1UL << i)) != 0x0)
                continue;

            entity.Comp.Index = i;

            var callsignName = Loc.GetString(set.Callsigns[i].Name);
            _metaDataSystem.SetEntityName(entity, entity.Comp.ReplacesName ? callsignName : $"{Name(entity)} {callsignName}");

            if (entity.Comp.NamesIdCard)
                _unnamedCards.Add((entity.Owner, callsignName, _gameTiming.CurTime + IdCardWait));

            return;
        }
    }

    private void OnMemberDowned(ref NpcSquadMemberDownedEvent args)
    {
        if (_calledOut.Contains(args.MemberUid) || !_squadQuery.TryComp(args.SquadUid, out var squadComponent))
            return;

        var downedCoordinates = _transformSystem.GetMapCoordinates(args.MemberUid);
        EntityUid? speakerUid = null;
        var nearest = float.MaxValue;

        foreach (var memberUid in squadComponent.Members)
        {
            if (memberUid == args.MemberUid || !_mobStateSystem.IsAlive(memberUid))
                continue;

            var memberCoordinates = _transformSystem.GetMapCoordinates(memberUid);
            if (memberCoordinates.MapId != downedCoordinates.MapId)
                continue;

            var distance = (memberCoordinates.Position - downedCoordinates.Position).Length();
            if (distance >= nearest ||
                distance > SeeDownRange ||
                !_npcLineOfSightSystem.InLineOfSight(memberCoordinates, downedCoordinates, SeeDownRange))
                continue;

            nearest = distance;
            speakerUid = memberUid;
        }

        if (speakerUid is not { } chosenUid)
            return;

        _calledOut.Add(args.MemberUid);
        LocId? line = TryGetCallsign(args.MemberUid, out var callsign) ? (LocId?) callsign.DownLine : null;
        _pending.Add((chosenUid, line, _gameTiming.CurTime + CalloutDelay));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;

        if (_unnamedCards.Count > 0 && now >= _nextCardCheck)
        {
            _nextCardCheck = now + CardCheckInterval;
            NameIdCards(now);
        }

        if (_pending.Count == 0 && _calledOut.Count == 0)
            return;

        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var (speakerUid, line, at) = _pending[i];
            if (now < at)
                continue;

            _pending.RemoveAt(i);

            if (TerminatingOrDeleted(speakerUid) || !_mobStateSystem.IsAlive(speakerUid))
                continue;

            if (line is { } downLine)
            {
                if (_npcVoiceSystem.MakesCallout(speakerUid, NpcCallout.ManDown))
                    _npcVoiceSystem.SpeakLine(speakerUid, downLine);
            }
            else
            {
                _npcVoiceSystem.TrySpeakCallout(speakerUid, NpcCallout.ManDown);
            }
        }

        if (now < _nextForget)
            return;

        _nextForget = now + ForgetInterval;
        _forget.Clear();

        // Back on its feet, it can go down again; gone, there is nothing to remember.
        foreach (var downedUid in _calledOut)
        {
            if (TerminatingOrDeleted(downedUid) || _mobStateSystem.IsAlive(downedUid))
                _forget.Add(downedUid);
        }

        foreach (var downedUid in _forget)
        {
            _calledOut.Remove(downedUid);
        }
    }

    private void NameIdCards(TimeSpan now)
    {
        for (var i = _unnamedCards.Count - 1; i >= 0; i--)
        {
            var (uid, name, until) = _unnamedCards[i];

            if (!TerminatingOrDeleted(uid))
            {
                if (_idCardSystem.TryFindIdCard(uid, out var idCard))
                    _idCardSystem.TryChangeFullName(idCard, name, idCard.Comp);
                else if (now < until)
                    continue;
            }

            _unnamedCards.RemoveAt(i);
        }
    }
}
