using System.Numerics;
using Content.Server._KS14.Dissolve;
using Content.Server._KS14.KillCredit;
using Content.Server._KS14.NPC.Targeting;
using Content.Shared._KS14.FarSound;
using Content.Shared._KS14.SupplyPod;
using Content.Shared._KS14.Mortar;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Mortar;

/// <summary>
///     Sets mortars up (<see cref="KsMortarCrewComponent"/>), fires them (<see cref="KsMortarComponent"/>), hands an abandoned one
///         to whoever picks it up, and dissolves it once nobody does, if it is one that dissolves. The shell does the rest: it is a supply pod, which marks
///         where it lands for as long as it falls.
/// </summary>
public sealed partial class KsMortarSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private FarSoundSystem _farSoundSystem = default!;
    [Dependency] private KsDissolveSystem _dissolveSystem = default!;
    [Dependency] private KsKillCreditSystem _killCreditSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private EntityQuery<KsMortarComponent> _mortarQuery = default!;
    [Dependency] private EntityQuery<KsMortarCrewComponent> _crewQuery = default!;
    [Dependency] private EntityQuery<KsNpcTargetPriorityComponent> _priorityQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<SupplyPodComponent> _supplyPodQuery = default!;
    [Dependency] private SharedSupplyPodSystem _supplyPodSystem = default!;

    /// <summary>
    ///     How soon to look again after finding nothing to fire at.
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Past this range, hostiles are found by going through every faction member rather than by a lookup over the whole
    ///         circle: at hundreds of tiles that lookup walks every entity on a good part of the map, each search.
    /// </summary>
    private const float LookupRangeLimit = 64f;

    private readonly List<(EntityUid Uid, Vector2 Position)> _candidates = new();
    private readonly HashSet<EntityUid> _hostiles = new();

    #region Crew

    [SubscribeLocalEvent]
    private void OnCrewMobStateChanged(Entity<KsMortarCrewComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && entity.Comp.MortarUid is { } mortarUid && _mortarQuery.TryComp(mortarUid, out var mortarComponent))
            Abandon((mortarUid, mortarComponent));
    }

    [SubscribeLocalEvent]
    private void OnCrewShutdown(Entity<KsMortarCrewComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.MortarUid is { } mortarUid &&
            !TerminatingOrDeleted(mortarUid) &&
            _mortarQuery.TryComp(mortarUid, out var mortarComponent) &&
            mortarComponent.CrewUid == entity.Owner)
            Abandon((mortarUid, mortarComponent));
    }

    private bool IsCrewAlive(EntityUid crewUid)
    {
        return !TerminatingOrDeleted(crewUid) &&
               (!_mobStateQuery.TryComp(crewUid, out var mobStateComponent) || _mobStateSystem.IsAlive(crewUid, mobStateComponent));
    }

    private bool IsMoving(EntityUid uid, float maxSpeed)
    {
        return _physicsQuery.TryComp(uid, out var physicsComponent) && physicsComponent.LinearVelocity.Length() > maxSpeed;
    }

    /// <summary>
    ///     Sets the crewman's mortar up where it stands, once it has stood still long enough with a hostile in the mortar's range.
    /// </summary>
    private void UpdateCrew(Entity<KsMortarCrewComponent> crew, TimeSpan now)
    {
        if (crew.Comp.Deployed || !IsCrewAlive(crew))
            return;

        if (!ProtoMan.TryIndex(crew.Comp.Mortar, out var mortarPrototype) ||
            !mortarPrototype.TryGetComponent<KsMortarComponent>(out var mortarTemplate, Factory))
            return;

        if (IsMoving(crew, mortarTemplate.MaxCrewSpeed))
        {
            crew.Comp.StillSince = null;
            return;
        }

        crew.Comp.StillSince ??= now;
        if (now - crew.Comp.StillSince < crew.Comp.SetUpTime ||
            now < crew.Comp.NextTargetSearch ||
            !_factionQuery.TryComp(crew, out var crewFactions))
            return;

        // Nothing in range: looked for again in a while, not next tick.
        if (!TryPickTarget(crew, (crew.Owner, crewFactions), mortarTemplate, out _, out _))
        {
            crew.Comp.NextTargetSearch = now + RetryDelay;
            return;
        }

        var mortarUid = Spawn(crew.Comp.Mortar, _transformSystem.GetMapCoordinates(crew));
        crew.Comp.Deployed = true;
        crew.Comp.MortarUid = mortarUid;

        if (_mortarQuery.TryComp(mortarUid, out var mortarComponent))
        {
            mortarComponent.CrewUid = crew.Owner;
            mortarComponent.IdleEndsAt = null;
            mortarComponent.NextFire = now;
            SetFactions(mortarUid, crewFactions.Factions);
        }

        var ev = new KsMortarDeployedEvent(mortarUid);
        RaiseLocalEvent(crew, ref ev);
    }

    #endregion

    #region Mortar

    /// <summary>
    ///     Nobody's until a crewman claims it, the same tick it sets it up: one put down any other way is anyone's from the
    ///         start, and, if it dissolves when abandoned, abandoned from the start.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsMortarComponent> entity, ref MapInitEvent args)
    {
        StartIdle(entity);
    }

    /// <summary>
    ///     Gives a mortar that dissolves when abandoned its idle time from now, or its short one once it has nothing left.
    /// </summary>
    private void StartIdle(Entity<KsMortarComponent> mortar)
    {
        if (!mortar.Comp.DissolveWhenAbandoned)
            return;

        mortar.Comp.IdleEndsAt = _gameTiming.CurTime + (mortar.Comp.Rounds > 0 ? mortar.Comp.IdleTime : mortar.Comp.EmptyTime);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsMortarComponent> entity, ref ComponentShutdown args)
    {
        CloseAllSights(entity.Comp);

        if (entity.Comp.CrewUid is { } crewUid)
            ReleaseCrew(entity, crewUid);
    }

    /// <summary>
    ///     Tells a crewman its mortar is no longer its own.
    /// </summary>
    private void ReleaseCrew(Entity<KsMortarComponent> mortar, EntityUid crewUid)
    {
        if (!_crewQuery.TryComp(crewUid, out var crewComponent) || TerminatingOrDeleted(crewUid) || crewComponent.MortarUid != mortar.Owner)
            return;

        crewComponent.MortarUid = null;
        var ev = new KsMortarLostEvent();
        RaiseLocalEvent(crewUid, ref ev);
    }

    // ExaminedEvent is a by-value class event, so the engine can't give this an Entity<T>.
    [SubscribeLocalEvent]
    private void OnExamined(EntityUid uid, KsMortarComponent component, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("ks-mortar-examine-rounds", ("rounds", component.Rounds)));
    }

    /// <summary>
    ///     Nobody but its own crewman fires a crewed mortar.
    /// </summary>
    // ActivatableUIOpenAttemptEvent is a by-value class event, so the engine can't give this an Entity<T>.
    [SubscribeLocalEvent]
    private void OnOpenAttempt(EntityUid uid, KsMortarComponent component, ActivatableUIOpenAttemptEvent args)
    {
        if (IsCrewed(component))
            args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnUiOpened(Entity<KsMortarComponent> entity, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey.Equals(KsMortarUiKey.Key))
            OpenSight(entity, args.Actor);

        RestartIdle(entity);
        UpdateUi(entity);
    }

    [SubscribeLocalEvent]
    private void OnFireMessage(Entity<KsMortarComponent> entity, ref KsMortarFireMessage args)
    {
        TryFireAt(entity, args.Actor, args.Bearing, args.Distance);
    }

    private bool IsCrewed(KsMortarComponent component)
    {
        return component.CrewUid is { } crewUid && IsCrewAlive(crewUid);
    }

    /// <summary>
    ///     Fires an abandoned mortar for <paramref name="userUid"/>, at a compass <paramref name="bearing"/> and
    ///         <paramref name="distance"/> in tiles, kept to its range. Refused on a crewed mortar, an empty one, one not yet
    ///         ready, and anything that is not a number. Returns whether it fired.
    /// </summary>
    public bool TryFireAt(Entity<KsMortarComponent> mortar, EntityUid userUid, Angle bearing, float distance)
    {
        var now = _gameTiming.CurTime;
        if (IsCrewed(mortar.Comp) ||
            mortar.Comp.Rounds <= 0 ||
            now < mortar.Comp.NextFire ||
            !double.IsFinite(bearing.Theta) ||
            !float.IsFinite(distance))
            return false;

        // A crewman only down, not dead, loses it to whoever fires it: it is theirs now, and never the crewman's again
        //      when it gets back up, which would have it shelling its own side.
        if (mortar.Comp.CrewUid is { } downedCrewUid)
        {
            Abandon(mortar);
            ReleaseCrew(mortar, downedCrewUid);
        }

        distance = Math.Clamp(distance, mortar.Comp.MinRange, mortar.Comp.MaxRange);

        // Zero is north, and it turns clockwise.
        var origin = _transformSystem.GetMapCoordinates(mortar);
        Launch(mortar, new MapCoordinates(origin.Position + KsMortarAim.ToOffset(bearing, distance), origin.MapId), userUid, now);

        // Whoever fires it is whose it is now.
        if (_factionQuery.TryComp(userUid, out var userFactions))
            SetFactions(mortar, userFactions.Factions);

        _audioSystem.PlayPvs(mortar.Comp.LoadSound, mortar);
        mortar.Comp.NextFire = now + mortar.Comp.ManualCooldown;
        RestartIdle(mortar);
        UpdateUi(mortar);
        return true;
    }

    /// <summary>
    ///     Spends a round on a shell landing within <see cref="KsMortarComponent.Spread"/> of <paramref name="aim"/>, whose
    ///         blast is <paramref name="firerUid"/>'s doing: the crewman's, or whoever fired an abandoned one. Landing names
    ///         nobody, and the log, kill credit and the Field Commander's ledger went by the shell alone.
    /// </summary>
    private void Launch(Entity<KsMortarComponent> mortar, MapCoordinates aim, EntityUid firerUid, TimeSpan now)
    {
        mortar.Comp.Rounds--;
        var landing = new MapCoordinates(aim.Position + _robustRandom.NextVector2(mortar.Comp.Spread), aim.MapId);

        // Spawned where it lands, falling for the whole flight, so that its landing zone is marked from now until it lands
        //      and it lands whatever becomes of the mortar; but seen falling only at the end of it, for as long as its
        //      incoming clip lasts, kept to the mortar's bounds.
        var flight = mortar.Comp.FlightTime;
        var shellUid = EntityManager.CreateEntityUninitialized(mortar.Comp.Shell, landing);
        var clipLength = (TimeSpan?) null;

        // The incoming clip ends as it lands, whichever one it is, over the end of the travel; one longer than the flight
        //      starts with it.
        if (mortar.Comp.IncomingSound is { } incomingSound)
        {
            var shellComponent = EnsureComp<KsMortarShellComponent>(shellUid);
            shellComponent.IncomingSound = _audioSystem.ResolveSound(incomingSound);
            var length = _audioSystem.GetAudioLength(shellComponent.IncomingSound);
            clipLength = length;
            shellComponent.IncomingAt = now + flight - (length < flight ? length : flight);
        }

        if (_supplyPodQuery.TryComp(shellUid, out var supplyPodComponent))
        {
            _supplyPodSystem.SetFallDuration((shellUid, supplyPodComponent), flight);
            _supplyPodSystem.SetDescentDuration((shellUid, supplyPodComponent), GetDescentTime(mortar.Comp, clipLength));
        }

        EnsureComp<KsMortarFallOfShotComponent>(shellUid).MortarUid = mortar.Owner;

        EntityManager.InitializeAndStartEntity(shellUid);
        _killCreditSystem.SetSender(shellUid, firerUid);
        _audioSystem.PlayPvs(mortar.Comp.TravelSound, Transform(shellUid).Coordinates);

        _audioSystem.PlayPvs(mortar.Comp.FireSound, mortar);
        _farSoundSystem.TryPlayFarSound(mortar, mortar.Comp.FarFireSound);

        if (mortar.Comp.Rounds <= 0 && mortar.Comp.IdleEndsAt is { } idleEndsAt && now + mortar.Comp.EmptyTime < idleEndsAt)
            mortar.Comp.IdleEndsAt = now + mortar.Comp.EmptyTime;
    }

    /// <summary>
    ///     How long a shell is seen falling: as long as its incoming clip, or the least it may be without one, kept between
    ///         <see cref="KsMortarComponent.MinDescentTime"/> and <see cref="KsMortarComponent.MaxDescentTime"/>, and to the
    ///         flight.
    /// </summary>
    public static TimeSpan GetDescentTime(KsMortarComponent mortarComponent, TimeSpan? clipLength)
    {
        var min = mortarComponent.MinDescentTime;
        var max = mortarComponent.MaxDescentTime < min ? min : mortarComponent.MaxDescentTime;
        var descent = clipLength ?? min;
        descent = descent < min ? min : descent > max ? max : descent;
        return descent > mortarComponent.FlightTime ? mortarComponent.FlightTime : descent;
    }

    /// <summary>
    ///     Its crewman dead or gone: it belongs to nobody, which its old side is hostile to, and, if it dissolves when
    ///         abandoned, does so unless used.
    /// </summary>
    private void Abandon(Entity<KsMortarComponent> mortar)
    {
        mortar.Comp.CrewUid = null;
        SetFactions(mortar, mortar.Comp.AbandonedFactions);
        EnsureComp<KsNpcTargetPriorityComponent>(mortar).Priority = mortar.Comp.AbandonedPriority;
        StartIdle(mortar);
    }

    /// <summary>
    ///     Used: an abandoned mortar lasts its idle time again from now, or its short one once it has nothing left.
    /// </summary>
    private void RestartIdle(Entity<KsMortarComponent> mortar)
    {
        if (IsCrewed(mortar.Comp))
            return;

        StartIdle(mortar);
    }

    private void SetFactions(EntityUid uid, HashSet<ProtoId<NpcFactionPrototype>> factions)
    {
        var target = new Entity<NpcFactionMemberComponent?>(uid, EnsureComp<NpcFactionMemberComponent>(uid));
        _npcFactionSystem.ClearFactions(target, dirty: false);
        _npcFactionSystem.AddFactions(target, new HashSet<ProtoId<NpcFactionPrototype>>(factions));
    }

    /// <summary>
    ///     Its shell down: where it landed goes on its mortar's fall of shot, if the mortar is still there to keep it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShellLanded(Entity<KsMortarFallOfShotComponent> entity, ref SupplyPodLandedEvent args)
    {
        var mortarUid = entity.Comp.MortarUid;
        if (TerminatingOrDeleted(mortarUid) || !_mortarQuery.TryComp(mortarUid, out var mortarComponent))
            return;

        var landing = _transformSystem.GetMapCoordinates(entity);
        var origin = _transformSystem.GetMapCoordinates(mortarUid);
        if (landing.MapId != origin.MapId)
            return;

        RecordImpact((mortarUid, mortarComponent), landing.Position - origin.Position);
    }

    /// <summary>
    ///     Keeps <paramref name="offset"/> (from the mortar) as its newest impact, dropping the oldest past its
    ///         <see cref="KsMortarComponent.MaxImpacts"/>.
    /// </summary>
    private void RecordImpact(Entity<KsMortarComponent> mortar, Vector2 offset)
    {
        var impacts = mortar.Comp.Impacts;
        impacts.Add(offset);

        var excess = impacts.Count - Math.Max(0, mortar.Comp.MaxImpacts);
        if (excess > 0)
            impacts.RemoveRange(0, excess);

        UpdateUi(mortar);
    }

    private void UpdateUi(Entity<KsMortarComponent> mortar)
    {
        _uiSystem.SetUiState(mortar.Owner,
            KsMortarUiKey.Key,
            new KsMortarBuiState(mortar.Comp.Rounds,
                mortar.Comp.MinRange,
                mortar.Comp.MaxRange,
                mortar.Comp.Spread,
                mortar.Comp.NextFire,
                mortar.Comp.ManualCooldown,
                new List<Vector2>(mortar.Comp.Impacts),
                mortar.Comp.SightRadius,
                GetSightEyes(mortar.Comp)));
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;

        var shellQuery = EntityQueryEnumerator<KsMortarShellComponent>();
        while (shellQuery.MoveNext(out var shellUid, out var shellComponent))
        {
            if (now < shellComponent.IncomingAt)
                continue;

            _audioSystem.PlayPvs(shellComponent.IncomingSound, Transform(shellUid).Coordinates);
            RemCompDeferred<KsMortarShellComponent>(shellUid);
        }

        var crewQuery = EntityQueryEnumerator<KsMortarCrewComponent>();
        while (crewQuery.MoveNext(out var crewUid, out var crewComponent))
            UpdateCrew((crewUid, crewComponent), now);

        var query = EntityQueryEnumerator<KsMortarComponent>();
        while (query.MoveNext(out var uid, out var mortarComponent))
        {
            if (mortarComponent.Sights.Count > 0)
                UpdateSights((uid, mortarComponent), now);

            if (mortarComponent.CrewUid is { } crewUid)
            {
                // A crewman down leaves it be, ready for whoever comes to it: put off a second at a time, it never was.
                if (now >= mortarComponent.NextFire && IsCrewAlive(crewUid))
                    mortarComponent.NextFire = TryCrewFire((uid, mortarComponent), crewUid, now) ? now + mortarComponent.Interval : now + RetryDelay;

                continue;
            }

            if (mortarComponent.IdleEndsAt is { } idleEndsAt && now >= idleEndsAt)
            {
                mortarComponent.IdleEndsAt = null;
                _dissolveSystem.Dissolve(uid);
            }
        }
    }

    /// <summary>
    ///     Fires at the best target in range for the crewman standing by it, if there is one. Returns whether it fired.
    /// </summary>
    private bool TryCrewFire(Entity<KsMortarComponent> mortar, EntityUid crewUid, TimeSpan now)
    {
        if (mortar.Comp.Rounds <= 0 ||
            !IsCrewAlive(crewUid) ||
            IsMoving(crewUid, mortar.Comp.MaxCrewSpeed) ||
            !_factionQuery.TryComp(crewUid, out var crewFactions))
            return false;

        var crewCoordinates = _transformSystem.GetMapCoordinates(crewUid);
        var origin = _transformSystem.GetMapCoordinates(mortar);
        if (crewCoordinates.MapId != origin.MapId ||
            (crewCoordinates.Position - origin.Position).Length() > mortar.Comp.CrewRange ||
            !TryPickTarget(mortar, (crewUid, crewFactions), mortar.Comp, out var targetPosition, out var mapId))
            return false;

        Launch(mortar, new MapCoordinates(targetPosition, mapId), crewUid, now);
        return true;
    }

    /// <summary>
    ///     The hostile to <paramref name="factions"/>, between the ranges of <paramref name="settings"/> from
    ///         <paramref name="fromUid"/>, most worth a shell: one more for each other hostile within its cluster radius, and
    ///         its target priority on top.
    /// </summary>
    private bool TryPickTarget(EntityUid fromUid, Entity<NpcFactionMemberComponent> factions, KsMortarComponent settings, out Vector2 position, out MapId mapId)
    {
        position = default;
        var origin = _transformSystem.GetMapCoordinates(fromUid);
        mapId = origin.MapId;

        var minRangeSquared = settings.MinRange * settings.MinRange;
        var maxRangeSquared = settings.MaxRange * settings.MaxRange;

        _candidates.Clear();
        _hostiles.Clear();
        if (settings.MaxRange <= LookupRangeLimit)
        {
            _npcFactionSystem.GetNearbyHostiles((factions.Owner, factions.Comp, null), settings.MaxRange, _hostiles);
        }
        else
        {
            var memberQuery = EntityQueryEnumerator<NpcFactionMemberComponent, TransformComponent>();
            while (memberQuery.MoveNext(out var memberUid, out _, out var memberTransform))
            {
                // Not in a container, on this map, and hostile; the range is checked below.
                if (memberTransform.MapID != origin.MapId ||
                    memberTransform.ParentUid != memberTransform.GridUid && memberTransform.ParentUid != memberTransform.MapUid ||
                    !_npcFactionSystem.IsEntityHostile((factions.Owner, factions.Comp, null), memberUid))
                    continue;

                _hostiles.Add(memberUid);
            }
        }

        foreach (var hostileUid in _hostiles)
        {
            if (_mobStateQuery.TryComp(hostileUid, out var hostileMobState) && _mobStateSystem.IsDead(hostileUid, hostileMobState))
                continue;

            var hostile = _transformSystem.GetMapCoordinates(hostileUid);
            if (hostile.MapId != origin.MapId)
                continue;

            var distanceSquared = (hostile.Position - origin.Position).LengthSquared();
            if (distanceSquared < minRangeSquared || distanceSquared > maxRangeSquared)
                continue;

            _candidates.Add((hostileUid, hostile.Position));
        }

        var clusterRadiusSquared = settings.ClusterRadius * settings.ClusterRadius;
        var bestScore = float.MinValue;
        var found = false;

        foreach (var (candidateUid, candidatePosition) in _candidates)
        {
            var score = 1f;
            foreach (var (otherUid, otherPosition) in _candidates)
            {
                if (otherUid != candidateUid && (otherPosition - candidatePosition).LengthSquared() <= clusterRadiusSquared)
                    score += 1f;
            }

            if (_priorityQuery.TryComp(candidateUid, out var priorityComponent))
                score += priorityComponent.Priority * settings.PriorityWeight;

            if (score <= bestScore)
                continue;

            bestScore = score;
            position = candidatePosition;
            found = true;
        }

        return found;
    }
}
