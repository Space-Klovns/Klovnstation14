using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Voices;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Callsigns;

/// <summary>
///     Hands out callsigns (<see cref="NpcCallsignComponent"/>), and has a squadmate call out the one going down: the
///         nearest that saw it, a moment later, by its callsign, or plainly for one with none. Only in a voice that calls
///         out men down (<see cref="NpcCallout.ManDown"/>); one nobody saw go down goes unsaid.
/// </summary>
public sealed partial class NpcCallsignSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
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
    ///     How long one called out stays called out: going critical and then dying is one man down, not two.
    /// </summary>
    private static readonly TimeSpan CalledOutMemory = TimeSpan.FromSeconds(60);

    private readonly List<(EntityUid SpeakerUid, EntityUid DownedUid, TimeSpan At)> _pending = new();
    private readonly Dictionary<EntityUid, TimeSpan> _calledOut = new();
    private readonly List<EntityUid> _forget = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NpcCallsignComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NpcSquadMemberDownedEvent>(OnMemberDowned);
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
            _metaDataSystem.SetEntityName(entity, $"{Name(entity)} {Loc.GetString(set.Callsigns[i].Name)}");
            return;
        }
    }

    private void OnMemberDowned(ref NpcSquadMemberDownedEvent args)
    {
        var now = _gameTiming.CurTime;

        if (_calledOut.TryGetValue(args.MemberUid, out var calledOutAt) && now - calledOutAt < CalledOutMemory ||
            !_squadQuery.TryComp(args.SquadUid, out var squadComponent))
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

        _calledOut[args.MemberUid] = now;
        _pending.Add((chosenUid, args.MemberUid, now + CalloutDelay));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0 && _calledOut.Count == 0)
            return;

        var now = _gameTiming.CurTime;

        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var (speakerUid, downedUid, at) = _pending[i];
            if (now < at)
                continue;

            _pending.RemoveAt(i);

            if (TerminatingOrDeleted(speakerUid) || !_mobStateSystem.IsAlive(speakerUid))
                continue;

            if (TryGetCallsign(downedUid, out var callsign))
            {
                if (_npcVoiceSystem.MakesCallout(speakerUid, NpcCallout.ManDown))
                    _npcVoiceSystem.SpeakLine(speakerUid, callsign.DownLine);
            }
            else
            {
                _npcVoiceSystem.TrySpeakCallout(speakerUid, NpcCallout.ManDown);
            }
        }

        _forget.Clear();
        foreach (var (downedUid, calledOutAt) in _calledOut)
        {
            if (now - calledOutAt >= CalledOutMemory)
                _forget.Add(downedUid);
        }

        foreach (var downedUid in _forget)
        {
            _calledOut.Remove(downedUid);
        }
    }
}
