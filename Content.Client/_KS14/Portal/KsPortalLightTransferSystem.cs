using System.Numerics;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Portal;
using Content.Shared._KS14.ZLevel.Light;
using Content.Shared.Examine;
using Content.Shared.Teleportation.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Portal;

/// <summary>
///     Experimental: carries light through view portals. Light reaching one portal's doorway shines out of
///         the linked portal's doorway, into the side opposite the one it came in from.
/// </summary>
/// <remarks>
///     Built the way z-levels shine light down through gaps in a floor (<c>KsZLevelLightLeakSystem</c>): the
///         engine cannot be handed a light that starts partway along its own beam, so each point sampled along
///         the far doorway gets a client-side stand-in point light, sized and dimmed by
///         <see cref="KsPortalLightTransfer"/> to carry on from where the real light reaches the near doorway.
///         Each stand-in wears a half-plane mask facing out of the exit side, so nothing behind the far portal
///         is lit, and casts shadows on its own side, so the far side's walls still shade it.
///     Stand-ins are never carried through again, so two portals facing each other cannot feed each other
///         light, and neither are z-level stand-ins, so the two features cannot chain.
/// </remarks>
public sealed partial class KsPortalLightTransferSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private ExamineSystemShared _examineSystem = default!;
    [Dependency] private PointLightSystem _pointLightSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsPortalViewComponent> _portalViewQuery = default!;
    [Dependency] private EntityQuery<LinkedEntityComponent> _linkedEntityQuery = default!;
    [Dependency] private EntityQuery<KsPortalLightTransferComponent> _transferQuery = default!;
    [Dependency] private EntityQuery<KsPortalLeakedLightComponent> _portalLeakedLightQuery = default!;
    [Dependency] private EntityQuery<KsZLevelLeakedLightComponent> _zLevelLeakedLightQuery = default!;
    [Dependency] private EntityQuery<PointLightComponent> _pointLightQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;

    private static readonly EntProtoId StandInPrototype = "KsPortalLeakedLight";
    private static readonly ProtoId<LightMaskPrototype> HalfPlaneMask = "KsPortalHalfPlane";

    /// <summary>
    ///     How far from a doorway, in tiles, lights are looked for. A light whose radius reaches further than
    ///         this from beyond it is not carried through.
    /// </summary>
    private const float SearchRange = 12f;

    /// <summary>
    ///     How near, in tiles, the viewer's eye has to be to either end of a link for its light to be carried.
    /// </summary>
    private const float RelevantRange = 32f;

    /// <summary>
    ///     A light this close to the line of a doorway, in tiles, is shining along it rather than into it, and
    ///         has no side to come out of.
    /// </summary>
    private const float MinimumSideDistance = 0.05f;

    /// <summary>
    ///     The most stand-ins one portal keeps; the brightest win.
    /// </summary>
    private const int MaximumStandInsPerPortal = 32;

    private bool _enabled;
    private bool _wasEnabled;
    private int _samples;
    private float _energyMultiplier;

    /// <summary>
    ///     Every portal this frame has to look at, gathered before any of them are touched, because spawning a
    ///         stand-in adds to the very component dictionaries an enumeration would be walking.
    /// </summary>
    private readonly List<EntityUid> _portalsToCheck = [];

    /// <summary>Scratch space, refilled for each portal in turn.</summary>
    private readonly HashSet<Entity<PointLightComponent>> _nearbyLights = [];
    private readonly List<WantedStandIn> _wantedStandIns = [];

    private readonly record struct WantedStandIn(
        EntityUid SourceUid,
        Vector2 Position,
        Angle MaskRotation,
        float Radius,
        float Energy);

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.PortalLightTransfer, value => _enabled = value, true);
        Subs.CVar(_configurationManager, KsCCVars.PortalLightTransferSamples, value => _samples = Math.Max(1, value), true);
        Subs.CVar(_configurationManager, KsCCVars.PortalLightTransferEnergy, value => _energyMultiplier = value, true);
    }

    /// <summary>
    ///     The component going is what deletes the stand-ins, whether that is this system dropping it or the
    ///         portal it belongs to being deleted outright.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnTransferShutdown(Entity<KsPortalLightTransferComponent> entity, ref ComponentShutdown args)
    {
        foreach (var standInUid in entity.Comp.StandIns)
        {
            if (!TerminatingOrDeleted(standInUid))
                Del(standInUid);
        }

        entity.Comp.StandIns.Clear();
    }

    /// <summary>
    ///     Driven per frame rather than per tick: the stand-ins are a rendering matter, and a tick update is
    ///         skipped or re-run by prediction in ways that have nothing to do with how often lighting changes.
    /// </summary>
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_enabled != _wasEnabled)
        {
            _wasEnabled = _enabled;

            if (!_enabled)
                ReleaseAllStandIns();
        }

        if (!_enabled)
            return;

        _portalsToCheck.Clear();

        var query = EntityQueryEnumerator<KsPortalViewComponent>();
        while (query.MoveNext(out var uid, out _))
            _portalsToCheck.Add(uid);

        foreach (var uid in _portalsToCheck)
        {
            // Gathered a moment ago rather than now, so it may have gone in between.
            if (_portalViewQuery.TryGetComponent(uid, out var portalViewComponent))
                UpdatePortal((uid, portalViewComponent));
        }
    }

    /// <summary>
    ///     Removes every stand-in, for when the experiment is switched off.
    /// </summary>
    private void ReleaseAllStandIns()
    {
        var query = AllEntityQuery<KsPortalLightTransferComponent>();
        while (query.MoveNext(out var uid, out var transferComponent))
            RemCompDeferred(uid, transferComponent);
    }

    private void UpdatePortal(Entity<KsPortalViewComponent> portal)
    {
        GetWantedStandIns(portal, out var destinationMapId);

        var transferComponent = _transferQuery.CompOrNull(portal.Owner);

        if (_wantedStandIns.Count == 0)
        {
            // Taken now rather than deferred, so a light switched off stops shining through on this frame.
            if (transferComponent != null)
                RemComp(portal.Owner, transferComponent);

            return;
        }

        transferComponent ??= EnsureComp<KsPortalLightTransferComponent>(portal.Owner);
        var standIns = transferComponent.StandIns;

        while (standIns.Count > _wantedStandIns.Count)
        {
            var lastIndex = standIns.Count - 1;
            var trimmedUid = standIns[lastIndex];
            standIns.RemoveAt(lastIndex);

            if (!TerminatingOrDeleted(trimmedUid))
                Del(trimmedUid);
        }

        for (var index = 0; index < _wantedStandIns.Count; index++)
        {
            if (index == standIns.Count)
                standIns.Add(SpawnStandIn());
            else if (TerminatingOrDeleted(standIns[index]))
                standIns[index] = SpawnStandIn();

            ApplyStandIn(standIns[index], _wantedStandIns[index], destinationMapId);
        }
    }

    /// <summary>
    ///     Fills <see cref="_wantedStandIns"/> with what every light reaching this portal's doorway should be
    ///         shining out of the linked portal's, brightest first, and leaves it empty if nothing should.
    /// </summary>
    private void GetWantedStandIns(Entity<KsPortalViewComponent> portal, out MapId destinationMapId)
    {
        _wantedStandIns.Clear();
        destinationMapId = MapId.Nullspace;

        if (!portal.Comp.Enabled ||
            !TryGetDestination(portal.Owner, out var destinationUid) ||
            !_transformQuery.TryGetComponent(portal.Owner, out var portalTransform) ||
            !_transformQuery.TryGetComponent(destinationUid, out var destinationTransform) ||
            portalTransform.MapID == MapId.Nullspace ||
            destinationTransform.MapID == MapId.Nullspace)
            return;

        destinationMapId = destinationTransform.MapID;

        var (portalPosition, portalRotation) = _transformSystem.GetWorldPositionRotation(portalTransform);
        var (destinationPosition, destinationRotation) = _transformSystem.GetWorldPositionRotation(destinationTransform);
        var mapId = portalTransform.MapID;
        var width = portal.Comp.Width;

        // The stand-ins can only be seen from near one end or the other: at the far end directly, or through
        //      the near end's window. Anywhere else, every lookup and ray cast below would be for nothing.
        var eyePosition = _eyeManager.CurrentEye.Position;
        if (!IsNear(eyePosition, mapId, portalPosition) && !IsNear(eyePosition, destinationMapId, destinationPosition))
            return;

        _nearbyLights.Clear();
        _entityLookupSystem.GetEntitiesInRange(
            new MapCoordinates(portalPosition, mapId),
            SearchRange + width * 0.5f,
            _nearbyLights);

        foreach (var light in _nearbyLights)
        {
            var lightComponent = light.Comp;

            if (!lightComponent.Enabled ||
                lightComponent.ContainerOccluded ||
                lightComponent.Radius <= 0f ||
                lightComponent.Energy <= 0f)
                continue;

            // Stand-ins are not lights in their own right. Carrying them through again would let two portals
            //      facing each other feed one another forever, and a z-level leak through a portal would chain
            //      the two features down the whole stack.
            if (_portalLeakedLightQuery.HasComponent(light.Owner) ||
                _zLevelLeakedLightQuery.HasComponent(light.Owner) ||
                _portalViewQuery.HasComponent(light.Owner))
                continue;

            var lightPosition = _transformSystem.GetWorldPosition(light.Owner);
            var lightLocal = KsPortalTransform.ToPortalFrame(lightPosition, portalPosition, portalRotation);

            if (MathF.Abs(lightLocal.Y) < MinimumSideDistance)
                continue;

            // Out of the opposite side to the one the light is on, as if the doorway were a hole.
            var exitDirection = destinationRotation.RotateVec(new Vector2(0f, -MathF.Sign(lightLocal.Y)));
            var maskRotation = KsPortalLightTransfer.GetMaskRotation(exitDirection);

            for (var index = 0; index < _samples; index++)
            {
                var sampleOffset = KsPortalLightTransfer.GetSampleOffset(index, _samples, width);
                var samplePosition = portalPosition + portalRotation.RotateVec(sampleOffset);
                var distance = (samplePosition - lightPosition).Length();

                if (!KsPortalLightTransfer.TryGetTransferredLight(
                        lightComponent.Radius,
                        lightComponent.Energy,
                        lightComponent.Falloff,
                        lightComponent.CurveFactor,
                        distance,
                        _samples,
                        out var transferredRadius,
                        out var transferredEnergy))
                    continue;

                // Only light that actually reaches the doorway goes through it.
                if (!_examineSystem.InRangeUnOccluded(
                        new MapCoordinates(lightPosition, mapId),
                        new MapCoordinates(samplePosition, mapId),
                        range: 0f,
                        predicate: null))
                    continue;

                _wantedStandIns.Add(new WantedStandIn(
                    light.Owner,
                    destinationPosition + destinationRotation.RotateVec(sampleOffset),
                    maskRotation,
                    transferredRadius,
                    transferredEnergy * _energyMultiplier));
            }
        }

        _wantedStandIns.Sort(static (first, second) => second.Energy.CompareTo(first.Energy));

        if (_wantedStandIns.Count > MaximumStandInsPerPortal)
            _wantedStandIns.RemoveRange(MaximumStandInsPerPortal, _wantedStandIns.Count - MaximumStandInsPerPortal);
    }

    private static bool IsNear(MapCoordinates eyePosition, MapId mapId, Vector2 position)
    {
        return eyePosition.MapId == mapId &&
               (eyePosition.Position - position).LengthSquared() <= RelevantRange * RelevantRange;
    }

    /// <summary>
    ///     The portal this one leads to, if it is linked to exactly one other view portal.
    /// </summary>
    private bool TryGetDestination(EntityUid portalUid, out EntityUid destinationUid)
    {
        destinationUid = EntityUid.Invalid;

        if (!_linkedEntityQuery.TryGetComponent(portalUid, out var linkedEntityComponent) ||
            linkedEntityComponent.LinkedEntities.Count != 1)
            return false;

        foreach (var linkedUid in linkedEntityComponent.LinkedEntities)
            destinationUid = linkedUid;

        return destinationUid != portalUid && _portalViewQuery.HasComponent(destinationUid);
    }

    private EntityUid SpawnStandIn()
    {
        var standInUid = Spawn(StandInPrototype);

        // Left on, it would attach itself to whatever grid it lands over and ride along with it, while the
        //      portal it stands at is placed by this system every frame anyway.
        _transformQuery.GetComponent(standInUid).GridTraversal = false;

        _pointLightSystem.SetMask(HalfPlaneMask, _pointLightQuery.GetComponent(standInUid));

        return standInUid;
    }

    private void ApplyStandIn(EntityUid standInUid, WantedStandIn wanted, MapId mapId)
    {
        if (!_pointLightQuery.TryGetComponent(standInUid, out var standInLight) ||
            !_pointLightQuery.TryGetComponent(wanted.SourceUid, out var sourceLight))
            return;

        var standInTransform = _transformQuery.GetComponent(standInUid);

        if (standInTransform.MapID != mapId ||
            !_transformSystem.GetWorldPosition(standInTransform).EqualsApprox(wanted.Position))
        {
            _transformSystem.SetMapCoordinates(standInUid, new MapCoordinates(wanted.Position, mapId));
        }

        // The mask auto-rotates with the entity, which is the only rotation of a light content may set.
        if (!_transformSystem.GetWorldRotation(standInTransform).EqualsApprox(wanted.MaskRotation))
            _transformSystem.SetWorldRotation(standInUid, wanted.MaskRotation);

        // Every one of these drops out on its own when the value has not moved.
        _pointLightSystem.SetRadius(standInUid, wanted.Radius, standInLight);
        _pointLightSystem.SetEnergy(standInUid, wanted.Energy, standInLight);
        _pointLightSystem.SetColor(standInUid, sourceLight.Color, standInLight);
        _pointLightSystem.SetSoftness(standInUid, sourceLight.Softness, standInLight);
        _pointLightSystem.SetOffset(standInUid, Vector2.Zero, standInLight);

        // Kept identical because the solve assumes them: the stand-in reproduces the real light's brightness
        //      by riding the same curve, not by being given a different one.
        _pointLightSystem.SetFalloff(standInUid, sourceLight.Falloff, standInLight);
        _pointLightSystem.SetCurveFactor(standInUid, sourceLight.CurveFactor, standInLight);

        // Shadows are cast by whatever is on the far side, which is right: what is in the way on the near side
        //      already decided whether the light reached the doorway at all.
        _pointLightSystem.SetCastShadows(standInUid, sourceLight.CastShadows, standInLight);

        _pointLightSystem.SetEnabled(standInUid, true, standInLight);
    }
}
