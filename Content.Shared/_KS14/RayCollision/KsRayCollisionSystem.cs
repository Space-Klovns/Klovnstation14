using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

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
///             what a collision means is for whoever opted it in. One a step: whatever the collision did to it (spent,
///             bounced, reflected) is for the next step to go on from.
///     </para>
///     <para>
///         The thing hit is remembered (<see cref="KsRayCollisionComponent.SweptHits"/>), so the contact physics may still make
///             with it is not a second collision: the consumer asks <see cref="WasSwept"/> in its own collision handler.
///     </para>
///     <para>
///         A ray down the middle, not the shape: it is for what would be stepped over. What it only grazes, physics finds
///             itself, as ever. Allocates only for a hit.
///     </para>
/// </remarks>
public sealed partial class KsRayCollisionSystem : EntitySystem
{
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
        return TryComp<KsRayCollisionComponent>(uid, out var component) && component.SweptHits.Contains(otherUid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var enumerator = EntityQueryEnumerator<KsRayCollisionComponent, PhysicsComponent, FixturesComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var rayCollisionComponent, out var physicsComponent, out var fixturesComponent, out var transformComponent))
        {
            if (!physicsComponent.CanCollide ||
                transformComponent.MapID == MapId.Nullspace ||
                !fixturesComponent.Fixtures.TryGetValue(rayCollisionComponent.Fixture, out var fixture))
                continue;

            var step = physicsComponent.LinearVelocity * frameTime;
            if (step.LengthSquared() < 0.0001f)
                continue;

            var origin = _transformSystem.GetWorldPosition(transformComponent);

            _sweeping = (uid, rayCollisionComponent, physicsComponent);
            _sweepingFixture = fixture;

            var result = _rayCastSystem.CastRayClosest(transformComponent.MapID,
                origin,
                step,
                new QueryFilter
                {
                    LayerBits = fixture.CollisionLayer,
                    MaskBits = fixture.CollisionMask,
                    IsIgnored = _isIgnored,
                });

            _sweeping = null;
            _sweepingFixture = null;

            if (!result.Hit)
                continue;

            var hit = result.Results[0];
            var length = step.Length();
            var point = origin + step * MathF.Max(0f, hit.Fraction - ContactMargin / length);

            rayCollisionComponent.SweptHits.Add(hit.Entity);
            _transformSystem.SetWorldPosition((uid, transformComponent), point);

            var ev = new KsRayCollisionEvent(hit.Entity, new MapCoordinates(point, transformComponent.MapID));
            RaiseLocalEvent(uid, ref ev);
        }
    }

    /// <summary>
    ///     What the entity being swept would not collide with: itself, what it already has, and anything a
    ///         <see cref="PreventCollideEvent"/> either way cancels, raised as physics raises it.
    /// </summary>
    private bool IsIgnored(EntityUid otherUid)
    {
        if (_sweeping is not { } sweeping || _sweepingFixture is not { } fixture)
            return true;

        if (otherUid == sweeping.Owner || sweeping.Comp1.SweptHits.Contains(otherUid))
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
