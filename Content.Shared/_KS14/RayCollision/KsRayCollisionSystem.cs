using System.Numerics;
using Content.Shared._KS14.PredictedSpawning;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.RayCollision;

/// <summary>
///     Continuous collision for the few things that need it. Physics has none (<c>SolveTOI</c> is a TODO): contacts are found
///         by overlap, step by step, so something moving more than its own length plus what it hits in a step can step clean
///         over that, or onto it and what is behind it at once, when whichever contact is handled first wins. A round does
///         both: at 40 tiles a second it moves more than a tile a tick, and a wall light's fixture is 0.15 of a tile deep.
/// </summary>
/// <remarks>
///     <para>
///         Before each physics step, every entity with <see cref="KsRayCollisionComponent"/> casts a ray along where it is
///             about to go (its velocity over the frame) for the nearest thing its fixture would collide with: by layer and
///             mask, only hard fixtures, and nothing a <see cref="PreventCollideEvent"/> either way cancels, as physics would
///             decide. If there is one, the entity is moved up to it, and <see cref="KsRayCollisionEvent"/> is raised on it:
///             what a collision means is for whoever opted it in. If it is still on its way after that (it went through,
///             or was reflected), the way it now goes is looked along as well, from where it was moved up to: physics
///             moves it a whole step from there, and that step would otherwise be one nothing looked along. At most
///             <see cref="MaxHitsPerStep"/> a step.
///     </para>
///     <para>
///         A ray finds what it enters, not what it starts inside. So the first time an entity is looked ahead of on a
///             flight, what its middle is already in is collided with first: someone stood on the muzzle is hit before
///             the wall behind them, as the contact physics makes there that same step would have had it.
///     </para>
///     <para>
///         The thing hit is remembered (<see cref="KsRayCollisionComponent.SweptHits"/>), so the contact physics may still make
///             with it is not a second collision: the consumer asks <see cref="WasSwept"/> in its own collision handler.
///             Only until it has left the thing behind (<see cref="ForgetLeftBehind"/>): one turned back onto something it
///             hit before hits it again.
///     </para>
///     <para>
///         A ray down the middle, not the shape: it is for what would be stepped over. What it only grazes, physics finds
///             itself, as ever. Allocates only for a hit.
///     </para>
///     <para>
///         <b>On a client</b> this runs on every predicted tick, the re-predicted ones too, and
///             <see cref="KsRayCollisionComponent.SweptHits"/> is not networked, so no state puts it back. Each hit is kept
///             with its tick, and a tick that is run again forgets what it and the ticks after it found: the entity has
///             been put back to before them, and is to hit the same things again, not pass through them for having hit
///             them the last time round. A client's kept copy of its own shot
///             (<see cref="KsSharedPredictedSpawnSystem.IsKeptLocally"/>) is never put back, and physics only moves it on
///             first-time ticks, so it is only looked ahead of on those.
///     </para>
/// </remarks>
public sealed partial class KsRayCollisionSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsSharedPredictedSpawnSystem _predictedSpawnSystem = default!;
    [Dependency] private RayCastSystem _rayCastSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    /// <summary>
    ///     How far short of what it hits the entity is put, along its way, so it is touching and not inside.
    /// </summary>
    private const float ContactMargin = 0.01f;

    /// <summary>
    ///     How many things an entity can collide with by looking ahead in one step: one that goes through what it hits, or
    ///         is turned round by it, is looked ahead of again from there.
    /// </summary>
    private const int MaxHitsPerStep = 4;

    /// <summary>
    ///     Half the side of the box about an entity's middle that is looked in for what it starts a flight inside.
    /// </summary>
    private const float StartOverlapExtent = 0.005f;

    /// <summary>
    ///     What an entity's middle is in at the start of its flight: scratch for <see cref="TryGetStartOverlap"/>.
    /// </summary>
    private readonly HashSet<EntityUid> _startOverlaps = new();

    /// <summary>
    ///     The entity being swept, its body and fixture, for <see cref="_isIgnored"/>: one delegate for every cast, not one a cast.
    /// </summary>
    private Entity<KsRayCollisionComponent, PhysicsComponent>? _sweeping;

    private Fixture? _sweepingFixture;

    private Func<EntityUid, bool> _isIgnored = default!;

    public override void Initialize()
    {
        base.Initialize();

        _isIgnored = IsIgnored;

        // Before the step that would carry it through.
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    /// <summary>
    ///     Starts looking ahead of <paramref name="uid"/> along the way its fixture <paramref name="fixtureId"/> goes, until
    ///         <see cref="StopChecking"/>.
    /// </summary>
    public void StartChecking(EntityUid uid, string fixtureId)
    {
        var component = EnsureComp<KsRayCollisionComponent>(uid);
        component.Fixture = fixtureId;
        component.SweptHits.Clear();
        component.Flying = false;
    }

    /// <summary>
    ///     Stops looking ahead of <paramref name="uid"/>. Deferred: it may be asked from a <see cref="KsRayCollisionEvent"/>
    ///         handler, while every entity checked is being gone through.
    /// </summary>
    public void StopChecking(EntityUid uid)
    {
        RemCompDeferred<KsRayCollisionComponent>(uid);
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> has already collided with <paramref name="otherUid"/> by looking ahead: a contact
    ///         physics starts with it after is the same collision, not another.
    /// </summary>
    public bool WasSwept(EntityUid uid, EntityUid otherUid)
    {
        return TryComp<KsRayCollisionComponent>(uid, out var component) && IsSwept(component, otherUid);
    }

    private static bool IsSwept(KsRayCollisionComponent component, EntityUid otherUid)
    {
        foreach (var (sweptUid, _) in component.SweptHits)
        {
            if (sweptUid == otherUid)
                return true;
        }

        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTick = _gameTiming.CurTick;
        var rePredicting = !_gameTiming.IsFirstTimePredicted; // never on the server

        var enumerator = EntityQueryEnumerator<KsRayCollisionComponent, PhysicsComponent, FixturesComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var rayCollisionComponent, out var physicsComponent, out var fixturesComponent, out var transformComponent))
        {
            if (rePredicting)
            {
                // A kept copy is not put back for this tick, and physics does not move it on it: nothing to look ahead of.
                if (_predictedSpawnSystem.IsKeptLocally(uid))
                    continue;

                // Put back to before this tick: what it hit on it, or after it, it has yet to hit.
                ForgetFrom(rayCollisionComponent, curTick);
            }

            ForgetLeftBehind((uid, rayCollisionComponent, fixturesComponent), curTick);

            Entity<KsRayCollisionComponent, PhysicsComponent, FixturesComponent, TransformComponent> entity =
                (uid, rayCollisionComponent, physicsComponent, fixturesComponent, transformComponent);

            for (var hits = 0; hits < MaxHitsPerStep; hits++)
            {
                if (!TryCollideAhead(entity, frameTime, curTick, first: hits == 0))
                    break;
            }
        }
    }

    /// <summary>
    ///     Forgets what <paramref name="component"/>'s entity hit on <paramref name="tick"/> or after it.
    /// </summary>
    private static void ForgetFrom(KsRayCollisionComponent component, GameTick tick)
    {
        var sweptHits = component.SweptHits;
        for (var i = sweptHits.Count - 1; i >= 0; i--)
        {
            if (sweptHits[i].Tick >= tick)
                sweptHits.RemoveAt(i);
        }
    }

    /// <summary>
    ///     Forgets what <paramref name="entity"/> hit before the last tick and is no longer touching. A hit is remembered so
    ///         that the contact physics makes with the same thing is not a second collision: that contact starts in the
    ///         step right after the look ahead, and lasts for as long as the two overlap (a round going through a wall, an
    ///         arrow sliding along one). Once it is over, the thing is behind it, and coming back to it - turned round by
    ///         something else - is a new collision, not one to pass through.
    /// </summary>
    private void ForgetLeftBehind(Entity<KsRayCollisionComponent, FixturesComponent> entity, GameTick curTick)
    {
        var sweptHits = entity.Comp1.SweptHits;
        for (var i = sweptHits.Count - 1; i >= 0; i--)
        {
            var (sweptUid, tick) = sweptHits[i];
            if (tick.Value + 1 >= curTick.Value || IsTouching((entity.Owner, entity.Comp2), sweptUid))
                continue;

            sweptHits.RemoveAt(i);
        }
    }

    private bool IsTouching(Entity<FixturesComponent> entity, EntityUid otherUid)
    {
        var contacts = _physicsSystem.GetContacts((entity.Owner, entity.Comp));
        while (contacts.MoveNext(out var contact))
        {
            if (contact.IsTouching && contact.OtherEnt(entity.Owner) == otherUid)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Looks along where <paramref name="entity"/> is about to go and collides it with the first thing on the way.
    /// </summary>
    /// <param name="first">Whether this is the first look of the step, rather than one after a collision in it.</param>
    /// <returns>Whether it collided with something and may still be on its way: there may be more to look along.</returns>
    private bool TryCollideAhead(Entity<KsRayCollisionComponent, PhysicsComponent, FixturesComponent, TransformComponent> entity,
        float frameTime,
        GameTick curTick,
        bool first)
    {
        var (uid, rayCollisionComponent, physicsComponent, fixturesComponent, transformComponent) = entity;

        // What the collision before this one in the step did to it: nothing more to look along for one that is going, or
        //      that is no longer looked ahead of.
        if (!first &&
            (rayCollisionComponent.LifeStage > ComponentLifeStage.Running ||
             TerminatingOrDeleted(uid) ||
             EntityManager.IsQueuedForDeletion(uid)))
            return false;

        var step = physicsComponent.LinearVelocity * frameTime;
        if (!physicsComponent.CanCollide ||
            transformComponent.MapID == MapId.Nullspace ||
            step.LengthSquared() < 0.0001f ||
            !fixturesComponent.Fixtures.TryGetValue(rayCollisionComponent.Fixture, out var fixture))
        {
            // Not on its way anywhere: whatever it hit was this flight's, and the next (an arrow pulled out and shot again)
            //      may hit the same things. Not for one a collision this step has just stopped: the contact physics makes
            //      with what stopped it, this same step, is that collision still.
            if (first)
            {
                rayCollisionComponent.SweptHits.Clear();
                rayCollisionComponent.Flying = false;
            }

            return false;
        }

        var origin = _transformSystem.GetWorldPosition(transformComponent);

        _sweeping = (uid, rayCollisionComponent, physicsComponent);
        _sweepingFixture = fixture;

        EntityUid hitUid;
        var point = origin;

        if (!rayCollisionComponent.Flying && TryGetStartOverlap(transformComponent.MapID, origin, out hitUid))
        {
            // Still not flying: each look finds one of what it starts inside, until none is left.
        }
        else
        {
            rayCollisionComponent.Flying = true;

            var result = _rayCastSystem.CastRayClosest(transformComponent.MapID,
                origin,
                step,
                new QueryFilter
                {
                    LayerBits = fixture.CollisionLayer,
                    MaskBits = fixture.CollisionMask,
                    IsIgnored = _isIgnored,
                });

            if (!result.Hit)
            {
                _sweeping = null;
                _sweepingFixture = null;
                return false;
            }

            var hit = result.Results[0];
            hitUid = hit.Entity;
            point = origin + step * MathF.Max(0f, hit.Fraction - ContactMargin / step.Length());
        }

        _sweeping = null;
        _sweepingFixture = null;

        rayCollisionComponent.SweptHits.Add((hitUid, curTick));

        if (point != origin)
            _transformSystem.SetWorldPosition((uid, transformComponent), point);

        var ev = new KsRayCollisionEvent(hitUid, new MapCoordinates(point, transformComponent.MapID));
        RaiseLocalEvent(uid, ref ev);
        return true;
    }

    /// <summary>
    ///     Something the entity being swept collides with whose fixture its middle, at <paramref name="origin"/>, is already
    ///         inside: what a ray from there would never find, starting inside it.
    /// </summary>
    private bool TryGetStartOverlap(MapId mapId, Vector2 origin, out EntityUid overlapUid)
    {
        overlapUid = default;

        _startOverlaps.Clear();
        _entityLookupSystem.GetEntitiesIntersecting(mapId,
            Box2.CenteredAround(origin, new Vector2(StartOverlapExtent * 2f)),
            _startOverlaps,
            LookupFlags.Dynamic | LookupFlags.Static);

        foreach (var candidateUid in _startOverlaps)
        {
            if (IsIgnored(candidateUid))
                continue;

            overlapUid = candidateUid;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     What the entity being swept would not collide with: itself, what it already has, and anything a
    ///         <see cref="PreventCollideEvent"/> either way cancels, raised as physics raises it.
    /// </summary>
    private bool IsIgnored(EntityUid otherUid)
    {
        if (_sweeping is not { } sweeping || _sweepingFixture is not { } fixture)
            return true;

        if (otherUid == sweeping.Owner || IsSwept(sweeping.Comp1, otherUid))
            return true;

        if (!_physicsQuery.TryComp(otherUid, out var otherBody) ||
            !otherBody.CanCollide ||
            GetHardFixture(otherUid, fixture) is not { } otherFixture)
            return true;

        var ours = new PreventCollideEvent(sweeping.Owner, otherUid, sweeping.Comp2, otherBody, fixture, otherFixture);
        RaiseLocalEvent(sweeping.Owner, ref ours);
        if (ours.Cancelled)
            return true;

        var theirs = new PreventCollideEvent(otherUid, sweeping.Owner, otherBody, sweeping.Comp2, otherFixture, fixture);
        RaiseLocalEvent(otherUid, ref theirs);
        return theirs.Cancelled;
    }

    /// <summary>
    ///     The hard fixture of <paramref name="otherUid"/> that <paramref name="fixture"/> collides with: the one a contact
    ///         would be made with.
    /// </summary>
    private Fixture? GetHardFixture(EntityUid otherUid, Fixture fixture)
    {
        if (!_fixturesQuery.TryComp(otherUid, out var fixturesComponent))
            return null;

        foreach (var otherFixture in fixturesComponent.Fixtures.Values)
        {
            if (otherFixture.Hard &&
                ((otherFixture.CollisionLayer & fixture.CollisionMask) != 0 || (otherFixture.CollisionMask & fixture.CollisionLayer) != 0))
                return otherFixture;
        }

        return null;
    }
}
