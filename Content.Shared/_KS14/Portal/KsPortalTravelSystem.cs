using System.Numerics;
using Content.Shared._KS14.CCVar;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Teleportation.Systems;
using Content.Shared.Weapons.Misc;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.ViewVariables;

namespace Content.Shared._KS14.Portal;

/// <summary>
///     Carries entities through <see cref="KsPortalViewComponent"/> doorways without anybody noticing: when an
///         entity's centre crosses the doorway segment it is put just as far beyond the linked doorway, turned
///         by the difference of the two doorways' rotations, with no effect, sound or timeout.
/// </summary>
/// <remarks>
///     Candidates are nominated by the portal's sensor fixture (<see cref="SensorFixtureId"/>) and tracked by a
///         <see cref="KsPortalTravellerComponent"/> that remembers, for every portal it is near, the side of
///         that portal's line the entity was last on.
///         Crossing is looked for after the physics step, from the side that changed and the position the
///         entity had one tick ago, which is its position minus its velocity times the tick. It runs in
///         prediction as well, for entities the client predicts. The entity lands past the linked doorway by
///         exactly as far as it was past this one, and its side is set relative to that doorway, so it cannot
///         cross straight back.
///     A doorway that faces whoever uses it (<see cref="KsPortalViewComponent.FacesViewer"/>) has a line of its
///         own for each traveller, turned square to where that traveller first came near, and kept until it leaves.
///         Player-made portals are these, and while they carry entities the stock portal's touch-to-teleport
///         stands aside (<see cref="CarriesTravellers"/>).
/// </remarks>
public sealed partial class KsPortalTravelSystem : EntitySystem
{
    /// <summary>
    ///     The id of the portal fixture whose contacts nominate travellers.
    /// </summary>
    public const string SensorFixtureId = "ksPortalSensor";

    /// <summary>
    ///     How far from the doorway, along and across it, an entity stays a candidate.
    /// </summary>
    private const float NearDistance = 3f;

    /// <summary>
    ///     How far the sensor reaches past the doorway's ends, and to either side of it.
    /// </summary>
    private const float SensorMargin = 1.5f;

    /// <summary>
    ///     How far from a <see cref="KsPortalViewComponent.FacesViewer"/> portal's centre an entity has to be before
    ///         its doorway can be turned to face it. Closer, as when a portal is opened under someone's feet,
    ///         there is no telling which way it should face, so nothing is crossed until the entity moves away.
    /// </summary>
    private const float MinimumFacingDistance = 0.2f;

    /// <summary>
    ///     What a sensor made in code collides with, for portals that come without one, as
    ///         <see cref="MakePlayerPortal"/> ones do. The same as the view portal prototype's.
    /// </summary>
    private const int SensorLayer = (int)(CollisionGroup.Impassable | CollisionGroup.HighImpassable | CollisionGroup.MidImpassable);
    private const int SensorMask = (int)(CollisionGroup.Impassable | CollisionGroup.HighImpassable | CollisionGroup.MidImpassable |
                                         CollisionGroup.LowImpassable | CollisionGroup.BulletImpassable);

    private bool _playerPortalViews;

    private readonly HashSet<EntityUid> _pendingSensorRefresh = [];
    private readonly List<EntityUid> _drainedSensorRefresh = [];

    /// <summary>
    ///     A traveller's portals, copied out before they are looked at, since crossing one rewrites them.
    /// </summary>
    private readonly List<EntityUid> _travellerPortals = [];

    [Dependency] private FixtureSystem _fixtureSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedJointSystem _jointSystem = default!;
    [Dependency] private PullingSystem _pullingSystem = default!;
    [Dependency] private LinkedEntitySystem _linkedEntitySystem = default!;
    [Dependency] private SharedMoverController _moverController = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IViewVariablesManager _viewVariablesManager = default!;
    [Dependency] private EntityQuery<KsPortalViewComponent> _viewQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;
    [Dependency] private EntityQuery<InputMoverComponent> _moverQuery = default!;
    [Dependency] private EntityQuery<PullableComponent> _pullableQuery = default!;
    [Dependency] private EntityQuery<PullerComponent> _pullerQuery = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        // The movement and physics of this tick have to be done before a crossing is looked for.
        UpdatesAfter.Add(typeof(SharedPhysicsSystem));

        // VV writes fields by reflection, past [Access], so Width is read-only in the VV window. This path is
        //      what vvwrite resolves instead of the field, and sends the edit through SetWidth.
        _viewVariablesManager.GetTypeHandler<KsPortalViewComponent>()
            .AddPath(
                nameof(KsPortalViewComponent.Width),
                (_, component) => component.Width,
                (uid, width, component) => SetWidth((uid, component), width));

        Subs.CVar(_configurationManager, KsCCVars.PortalPlayerViews, OnPlayerViewsChanged, true);
    }

    /// <summary>
    ///     Switches every player-made portal over at once, so the setting takes effect without new portals.
    /// </summary>
    private void OnPlayerViewsChanged(bool value)
    {
        _playerPortalViews = value;

        // Enabled is networked; the client is told rather than working it out.
        if (_netManager.IsClient)
            return;

        var query = AllEntityQuery<KsPortalViewComponent>();
        while (query.MoveNext(out var uid, out var viewComponent))
            ApplyPlayerViews((uid, viewComponent));
    }

    /// <summary>
    ///     Brings a player-made portal's <see cref="KsPortalViewComponent.Enabled"/> in line with
    ///         <see cref="KsCCVars.PortalPlayerViews"/>. Anything else is left alone.
    /// </summary>
    private void ApplyPlayerViews(Entity<KsPortalViewComponent> entity)
    {
        if (!entity.Comp.PlayerMade || entity.Comp.Enabled == _playerPortalViews)
            return;

        entity.Comp.Enabled = _playerPortalViews;
        Dirty(entity);
    }

    /// <summary>
    ///     Turns a portal a player has just made, such as one from the hand teleporter, into a view portal whose
    ///         doorway faces whoever uses it, while <see cref="KsCCVars.PortalPlayerViews"/> is on. Server only.
    /// </summary>
    public void MakePlayerPortal(EntityUid portalUid)
    {
        if (_netManager.IsClient || TerminatingOrDeleted(portalUid))
            return;

        var viewComponent = EnsureComp<KsPortalViewComponent>(portalUid);
        viewComponent.FacesViewer = true;
        viewComponent.PlayerMade = true;
        viewComponent.Enabled = _playerPortalViews;
        Dirty(portalUid, viewComponent);

        // Made after the portal was set up, so the sensor is made now rather than at map init.
        RefreshSensor((portalUid, viewComponent));
    }

    /// <summary>
    ///     Whether this portal carries entities through its doorway itself: it is enabled and leads to another
    ///         enabled view portal. The stock portal behaviour has to stand aside for such a portal, or touching
    ///         it would teleport an entity before it reached the doorway.
    /// </summary>
    public bool CarriesTravellers(EntityUid portalUid)
    {
        return _viewQuery.TryComp(portalUid, out var viewComponent)
               && viewComponent.Enabled
               && _linkedEntitySystem.GetLink(portalUid, out var destinationUid)
               && destinationUid != portalUid
               && _viewQuery.TryComp(destinationUid, out var destinationView)
               && destinationView.Enabled;
    }

    public override void Shutdown()
    {
        // The VV manager outlives this system, and its handler would keep it alive.
        _viewVariablesManager.GetTypeHandler<KsPortalViewComponent>().RemovePath(nameof(KsPortalViewComponent.Width));

        base.Shutdown();
    }

    [SubscribeLocalEvent]
    private void OnViewMapInit(Entity<KsPortalViewComponent> entity, ref MapInitEvent args)
    {
        // Not resized in place: fixtures cannot safely be swapped while the entity is still being set up.
        _pendingSensorRefresh.Add(entity.Owner);

        if (_netManager.IsServer)
            ApplyPlayerViews(entity);
    }

    /// <summary>
    ///     Changes the length of a portal's doorway, resizing the sensor that nominates travellers to match.
    /// </summary>
    public void SetWidth(Entity<KsPortalViewComponent?> entity, float width)
    {
        if (!_viewQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false) ||
            MathHelper.CloseTo(entity.Comp.Width, width))
            return;

        entity.Comp.Width = width;
        Dirty(entity.Owner, entity.Comp);

        RefreshSensor((entity.Owner, entity.Comp));
    }

    /// <summary>
    ///     Sizes the portal's sensor fixture to its <see cref="KsPortalViewComponent.Width"/>: the doorway plus
    ///         <see cref="SensorMargin"/> past each end, and the same to either side, or a circle reaching as far
    ///         for a doorway that faces whoever uses it. A portal without a sensor fixture is given one, if it has a
    ///         physics body to put it on. Server only: the fixtures reach the client by state, and it must not
    ///         build its own.
    /// </summary>
    private void RefreshSensor(Entity<KsPortalViewComponent> entity)
    {
        if (_netManager.IsClient)
            return;

        var fixture = _fixtureSystem.GetFixtureOrNull(entity.Owner, SensorFixtureId);
        if (fixture == null && !HasComp<PhysicsComponent>(entity.Owner))
            return;

        var desired = GetSensorBounds(entity.Comp.Width);
        var circular = entity.Comp.FacesViewer;

        if (fixture != null)
        {
            // Compared by extent, as the shape may be the prototype's box or the polygon this made before.
            var current = fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0);
            var expectedTop = circular ? desired.Right : desired.Top;
            if ((fixture.Shape is PhysShapeCircle) == circular &&
                MathF.Abs(current.Right - desired.Right) < 0.05f &&
                MathF.Abs(current.Top - expectedTop) < 0.05f)
                return;
        }

        IPhysShape shape;
        if (circular)
        {
            shape = new PhysShapeCircle(desired.Right);
        }
        else
        {
            var polygonShape = new PolygonShape();
            polygonShape.SetAsBox(desired);
            shape = polygonShape;
        }

        var layer = fixture?.CollisionLayer ?? SensorLayer;
        var mask = fixture?.CollisionMask ?? SensorMask;
        var hard = fixture?.Hard ?? false;
        var density = fixture?.Density ?? 1f;

        // Destroying the body's last fixture switches its collision off, and the new one would not be
        //      found by anything until it is switched back on.
        var couldCollide = TryComp<PhysicsComponent>(entity.Owner, out var bodyComponent) && bodyComponent.CanCollide;

        if (fixture != null)
            _fixtureSystem.DestroyFixture(entity.Owner, SensorFixtureId, fixture);

        _fixtureSystem.TryCreateFixture(
            entity.Owner,
            shape,
            SensorFixtureId,
            density: density,
            hard: hard,
            collisionLayer: layer,
            collisionMask: mask);

        if (couldCollide)
            _physicsSystem.SetCanCollide(entity.Owner, true, body: bodyComponent);
    }

    /// <summary>
    ///     The sensor's bounds in the portal's frame for a doorway of the given width.
    /// </summary>
    public static Box2 GetSensorBounds(float width)
    {
        var halfExtent = width * 0.5f + SensorMargin;
        return new Box2(-halfExtent, -SensorMargin, halfExtent, SensorMargin);
    }

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsPortalViewComponent> entity, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != SensorFixtureId || args.OtherBody.BodyType == BodyType.Static)
            return;

        var travellerUid = args.OtherEntity;
        if (!_transformQuery.TryComp(travellerUid, out var travellerTransform) || travellerTransform.Anchored)
            return;

        var travellerComponent = EnsureComp<KsPortalTravellerComponent>(travellerUid);
        if (travellerComponent.Sides.ContainsKey(entity.Owner))
            return;

        // A doorway that faces whoever uses it is turned on the first look, in CheckPortal; until then the side
        //      is not known.
        travellerComponent.Sides[entity.Owner] = entity.Comp.FacesViewer ? 0 : GetSide(entity.Owner, travellerUid, doorwayRotation: null);
        Dirty(travellerUid, travellerComponent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingSensorRefresh.Count > 0)
        {
            _drainedSensorRefresh.Clear();
            _drainedSensorRefresh.AddRange(_pendingSensorRefresh);
            _pendingSensorRefresh.Clear();

            foreach (var portalUid in _drainedSensorRefresh)
            {
                if (_viewQuery.TryComp(portalUid, out var pendingView) && !TerminatingOrDeleted(portalUid))
                    RefreshSensor((portalUid, pendingView));
            }
        }

        var tickSeconds = (float)_gameTiming.TickPeriod.TotalSeconds;

        var travellerEnumerator = EntityQueryEnumerator<KsPortalTravellerComponent, TransformComponent, PhysicsComponent>();
        while (travellerEnumerator.MoveNext(out var travellerUid, out var travellerComponent, out var travellerTransform, out var physicsComponent))
        {
            // The client only moves what it predicts; the rest arrives already placed by the server.
            if (_netManager.IsClient && !physicsComponent.Predict)
                continue;

            if (travellerTransform.Anchored)
            {
                RemCompDeferred<KsPortalTravellerComponent>(travellerUid);
                continue;
            }

            _travellerPortals.Clear();
            _travellerPortals.AddRange(travellerComponent.Sides.Keys);

            foreach (var portalUid in _travellerPortals)
            {
                if (CheckPortal((travellerUid, travellerComponent, travellerTransform, physicsComponent), portalUid, tickSeconds))
                    break; // Carried through: its portals were replaced by the one it came out of.
            }

            if (travellerComponent.Sides.Count == 0)
                RemCompDeferred<KsPortalTravellerComponent>(travellerUid);
        }
    }

    /// <summary>
    ///     Looks for the traveller crossing one portal's doorway since the last tick, and carries it through if it
    ///         has. Forgets the portal once the traveller is no longer near it.
    /// </summary>
    /// <returns>Whether the traveller was carried through.</returns>
    private bool CheckPortal(
        Entity<KsPortalTravellerComponent, TransformComponent, PhysicsComponent> traveller,
        EntityUid portalUid,
        float tickSeconds)
    {
        var travellerComponent = traveller.Comp1;

        if (!_viewQuery.TryComp(portalUid, out var viewComponent)
            || !_transformQuery.TryComp(portalUid, out var portalTransform)
            || portalTransform.MapID != traveller.Comp2.MapID)
        {
            ForgetPortal(traveller, portalUid);
            return false;
        }

        var (portalPosition, portalRotation) = _transformSystem.GetWorldPositionRotation(portalTransform);
        var travellerPosition = _transformSystem.GetWorldPosition(traveller.Comp2);

        // Still on the centre of a doorway that faces whoever uses it: there is no telling which way it faces yet.
        if (!TryGetDoorwayRotation(traveller, portalUid, viewComponent, portalPosition, portalRotation, travellerPosition, out var doorwayRotation))
            return false;

        var local = KsPortalTransform.ToPortalFrame(travellerPosition, portalPosition, doorwayRotation);
        var halfWidth = viewComponent.Width * 0.5f;

        if (MathF.Abs(local.Y) > NearDistance || MathF.Abs(local.X) > halfWidth + NearDistance)
        {
            ForgetPortal(traveller, portalUid);
            return false;
        }

        var side = Math.Sign(local.Y);
        var lastSide = travellerComponent.Sides[portalUid];
        if (side == 0 || side == lastSide)
            return false;

        if (lastSide != 0)
        {
            // The side changed. It only counts as a crossing when it happened through the doorway, between
            //      where the entity was a tick ago and where it is now.
            var localVelocity = (-doorwayRotation).RotateVec(traveller.Comp3.LinearVelocity);
            var previous = local - localVelocity * tickSeconds;

            var withinDoorway = false;
            if (Math.Sign(previous.Y) != side)
            {
                var span = previous.Y - local.Y;
                var fraction = MathF.Abs(span) > float.Epsilon ? previous.Y / span : 0f;
                var crossingX = previous.X + fraction * (local.X - previous.X);
                withinDoorway = MathF.Abs(crossingX) <= halfWidth;
            }

            if (withinDoorway
                && viewComponent.Enabled
                && TryTravel(traveller, portalUid, portalPosition, portalRotation, doorwayRotation))
                return true;
        }

        // Around the doorway, or nowhere to go: just remember the new side.
        travellerComponent.Sides[portalUid] = side;
        Dirty(traveller.Owner, travellerComponent);
        return false;
    }

    /// <summary>
    ///     The rotation of the portal's doorway for this traveller: the portal's own, or, for a doorway that faces
    ///         whoever uses it, the one it was given when the traveller first came near enough to tell, which is
    ///         chosen here if it has not been yet.
    /// </summary>
    /// <returns>False while the traveller is too close to the centre of a facing doorway for it to face anywhere.</returns>
    private bool TryGetDoorwayRotation(
        Entity<KsPortalTravellerComponent, TransformComponent, PhysicsComponent> traveller,
        EntityUid portalUid,
        KsPortalViewComponent viewComponent,
        Vector2 portalPosition,
        Angle portalRotation,
        Vector2 travellerPosition,
        out Angle doorwayRotation)
    {
        doorwayRotation = portalRotation;
        if (!viewComponent.FacesViewer)
            return true;

        var travellerComponent = traveller.Comp1;
        if (travellerComponent.DoorwayRotations.TryGetValue(portalUid, out doorwayRotation))
            return true;

        var offset = travellerPosition - portalPosition;
        if (offset.Length() < MinimumFacingDistance)
            return false;

        // Square to the traveller, so its side is known (+1) and walking on in crosses the doorway.
        doorwayRotation = KsPortalTransform.GetFacingRotation(offset);
        travellerComponent.DoorwayRotations[portalUid] = doorwayRotation;
        travellerComponent.Sides[portalUid] = 0;
        Dirty(traveller.Owner, travellerComponent);
        return true;
    }

    private void ForgetPortal(Entity<KsPortalTravellerComponent> traveller, EntityUid portalUid)
    {
        var removedSide = traveller.Comp.Sides.Remove(portalUid);
        var removedRotation = traveller.Comp.DoorwayRotations.Remove(portalUid);

        if (removedSide || removedRotation)
            Dirty(traveller);
    }

    /// <param name="portalRotation">The portal's own rotation, which is what where the traveller lands follows.</param>
    /// <param name="doorwayRotation">The rotation of the doorway it crossed, which differs for a facing doorway.</param>
    private bool TryTravel(
        Entity<KsPortalTravellerComponent, TransformComponent, PhysicsComponent> traveller,
        EntityUid portalUid,
        Vector2 portalPosition,
        Angle portalRotation,
        Angle doorwayRotation)
    {
        if (!_linkedEntitySystem.GetLink(portalUid, out var destinationUid)
            || destinationUid == portalUid
            || TerminatingOrDeleted(destinationUid.Value)
            || !_viewQuery.TryComp(destinationUid, out var destinationView)
            || !destinationView.Enabled
            || !_transformQuery.TryComp(destinationUid, out var destinationTransform)
            || destinationTransform.MapID == MapId.Nullspace)
            return false;

        // Entities that are parented to anything but the map or a grid (buckled, carried) are not ours to move.
        var travellerTransform = traveller.Comp2;
        if (travellerTransform.ParentUid != travellerTransform.MapUid && travellerTransform.ParentUid != travellerTransform.GridUid)
            return false;

        // A destination outside what the client has been sent cannot be predicted; the server will do it.
        if (_netManager.IsClient && (MetaData(destinationUid.Value).Flags & MetaDataFlags.Detached) != 0)
            return false;

        var (destinationPosition, destinationRotation) = _transformSystem.GetWorldPositionRotation(destinationTransform);
        var (travellerPosition, travellerRotation) = _transformSystem.GetWorldPositionRotation(travellerTransform);

        var relativeRotation = KsPortalTransform.GetRelativeRotation(portalRotation, destinationRotation);
        var newPosition = KsPortalTransform.MapPoint(travellerPosition, portalPosition, portalRotation, destinationPosition, destinationRotation);
        var newRotation = travellerRotation + relativeRotation;
        var newVelocity = relativeRotation.RotateVec(traveller.Comp3.LinearVelocity);

        // Pulls are broken before the move, as in the stock portal, so nothing is dragged or yanked across.
        if (_pullableQuery.TryComp(traveller.Owner, out var pullableComponent) && pullableComponent.BeingPulled)
            _pullingSystem.TryStopPull(traveller.Owner, pullableComponent);

        if (_pullerQuery.TryComp(traveller.Owner, out var pullerComponent)
            && pullerComponent.Pulling is { } pulledUid
            && _pullableQuery.TryComp(pulledUid, out var pulledComponent))
            _pullingSystem.TryStopPull(pulledUid, pulledComponent);

        _jointSystem.RemoveJoint(traveller.Owner, SharedGrapplingGunSystem.GrapplingJoint);

        // Shot things should be able to hit whoever fired them once they have come out of a portal.
        if (_projectileQuery.TryComp(traveller.Owner, out var projectileComponent))
            projectileComponent.IgnoreShooter = false;

        var hadMover = _moverQuery.TryComp(traveller.Owner, out var moverComponent);
        var previousParentAngle = hadMover ? _moverController.GetParentGridAngle(moverComponent!) : Angle.Zero;

        _transformSystem.SetMapCoordinates((traveller.Owner, travellerTransform), new MapCoordinates(newPosition, destinationTransform.MapID));
        _transformSystem.SetWorldRotationNoLerp((traveller.Owner, travellerTransform), newRotation);
        _physicsSystem.SetLinearVelocity(traveller.Owner, newVelocity, body: traveller.Comp3);

        // The move and the turn happened without a lerp, but an entity that was already lerping would carry on
        //      from where it was a tick ago, across the map. Restart it from here: setting the position it
        //      already has through the lerping setter leaves nothing to lerp to.
        travellerTransform.ActivelyLerping = false;
        _transformSystem.SetLocalPositionRotation(traveller.Owner, travellerTransform.LocalPosition, travellerTransform.LocalRotation, travellerTransform);

        if (hadMover)
        {
            // Wish direction and camera both hang on the mover's parent angle. Turning it by the same amount as
            //      the entity keeps "up" going the same way on screen and the picture unchanged, whether or not
            //      the parent change of this move has just reset it.
            var correction = previousParentAngle + relativeRotation - _moverController.GetParentGridAngle(moverComponent!);
            moverComponent!.RelativeRotation += correction;
            moverComponent.TargetRelativeRotation += correction;
            Dirty(traveller.Owner, moverComponent);
        }

        // Every other portal it was near is behind it now. The one it came out of is watched from the side it
        //      came out on, so it cannot cross straight back. A facing doorway there is the one just crossed,
        //      carried over, so the traveller is past it rather than in front of a freshly turned one.
        traveller.Comp1.Sides.Clear();
        traveller.Comp1.DoorwayRotations.Clear();

        Angle? destinationDoorwayRotation = null;
        if (destinationView.FacesViewer)
        {
            destinationDoorwayRotation = doorwayRotation + relativeRotation;
            traveller.Comp1.DoorwayRotations[destinationUid.Value] = destinationDoorwayRotation.Value;
        }

        traveller.Comp1.Sides[destinationUid.Value] = GetSide(destinationUid.Value, traveller.Owner, destinationDoorwayRotation);
        Dirty(traveller.Owner, traveller.Comp1);

        var travelledEvent = new KsPortalTravelledEvent(relativeRotation);
        RaiseLocalEvent(traveller.Owner, ref travelledEvent);

        return true;
    }

    /// <summary>
    ///     Which side of the portal's line the entity is on, as -1, 0 (on the line) or 1.
    /// </summary>
    /// <param name="doorwayRotation">The doorway's rotation, if not the portal's own.</param>
    private int GetSide(EntityUid portalUid, EntityUid travellerUid, Angle? doorwayRotation)
    {
        if (!_transformQuery.TryComp(portalUid, out var portalTransform)
            || !_transformQuery.TryComp(travellerUid, out var travellerTransform)
            || portalTransform.MapID != travellerTransform.MapID)
            return 0;

        var (portalPosition, portalRotation) = _transformSystem.GetWorldPositionRotation(portalTransform);
        var local = KsPortalTransform.ToPortalFrame(
            _transformSystem.GetWorldPosition(travellerTransform),
            portalPosition,
            doorwayRotation ?? portalRotation);

        return Math.Sign(local.Y);
    }
}
