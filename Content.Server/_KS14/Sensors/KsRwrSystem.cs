using Content.Shared._KS14.Sensors;
using Robust.Shared.Map;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Answers sensor sweeps for <see cref="KsRwrComponent"/>. The RWR is the
///         defensive tripwire below the ELINT array: it hears only emissions that
///         actually ILLUMINATE its own grid (a foreign radar cone with line of
///         sight onto the hull, a foreign jam slice covering the centre of mass)
///         and files each as a bare bearing of the emitting grid, with the heard
///         band/pattern/strength as identification intel. No sensitivity tuning,
///         no focus analysis, and deliberately NO self-blind: a warning receiver
///         must keep warning while the own radar is up, and it never reports the
///         own grid's emissions in the first place.
///     Because a radar's cone reaches ~twice its detection range, the RWR warns
///         the crew while the painting radar still cannot resolve them. The
///         detections ride the pool and datalink like any other, so an ally's RWR
///         pings the whole fleet.
/// </summary>
public sealed partial class KsRwrSystem : KsEmissionListenerSystem
{
    [Dependency] private KsSensorSystem _sensors = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsRwrComponent, KsSensorSweepEvent>(OnSweep);
    }

    private void OnSweep(Entity<KsRwrComponent> ent, ref KsSensorSweepEvent args)
    {
        var xform = Transform(args.Sensor);
        if (xform.MapID == MapId.Nullspace || xform.GridUid is not { } ownGrid)
            return;

        if (!GridQuery.TryGetComponent(ownGrid, out var ownGridComp)
            || !PhysicsQuery.TryGetComponent(ownGrid, out var ownPhysics))
            return;

        var mapId = xform.MapID;
        var (ownPos, ownRot) = XformSystem.GetWorldPositionRotation(ownGrid);
        var ownCom = ownPos + ownRot.RotateVec(ownPhysics.LocalCenter);
        var sensorPos = XformSystem.GetWorldPosition(xform);

        // Radar emitters. Heard exactly when they illuminate us: our grid inside the
        // cone's full reach with a clear line of sight from the emitter (the same beam
        // geometry the ELINT uses; a hull in the radar's shadow is not being painted).
        foreach (var radar in _sensors.RadarEmissions)
        {
            if (radar.MapId != mapId || radar.Grid == ownGrid)
                continue;

            if (!IsAnyPartVisible(mapId, radar.Pos, radar.ConeReach, ownGrid, ownGridComp.LocalAABB, ownPos, ownRot))
                continue;

            args.Detections.Add(BuildEmitterDetection(radar.Grid, intel: null, typeOverride: null,
                radar.Band, radar.Pattern, SignalStrength(sensorPos, radar.Pos, radar.ConeReach)));
        }

        // Jammer emitters. Heard when the jam slice covers our centre of mass, with NO
        // line of sight test (the same loud-broadband asymmetry as ELINT's jammer
        // path). Classified as a jammer return so the Rwr tier never strips the
        // magenta jammer classification off a co-tracked contact.
        foreach (var jammer in _sensors.JammerEmissions)
        {
            if (jammer.MapId != mapId || jammer.Grid == ownGrid)
                continue;

            if (!jammer.Contains(ownCom, jammer.Power))
                continue;

            args.Detections.Add(BuildEmitterDetection(jammer.Grid, intel: null, typeOverride: KsSensorType.Jammer,
                jammer.Band, jammer.Pattern, SignalStrength(sensorPos, jammer.Pos, jammer.Power)));
        }
    }
}
