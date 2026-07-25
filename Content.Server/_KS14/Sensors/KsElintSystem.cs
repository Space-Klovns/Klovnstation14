using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Answers sensor sweeps for <see cref="KsElintComponent"/>. ELINT is a passive
///         listener: it emits nothing and detects no grids by their own signature. It
///         reads the active-emission registry
///         (<see cref="KsSensorSystem.RadarEmissions"/> / JammerEmissions) and, for each
///         emitter cone it sits inside, produces a contact of the emitting grid: an
///         orange radar return, or a magenta jammer return. The detection carries the
///         emitter's true position (the pool needs the truth for dedup, ghost pruning
///         and triangulation), but the shipped array resolves only a BEARING
///         (<c>resolvesPosition: Bearing</c> in YAML), so consoles get a direction
///         strobe, not a fix, until datalink triangulation (or completed focus
///         analysis) earns one. It also carries the heard emission's band, pattern and
///         relative signal strength as identification intel.
///     Detection reach scales with <see cref="KsElintComponent.IgnoreFraction"/>: a
///         sensitive ELINT hears an emitter across nearly its whole cone (for a radar,
///         out to ~twice its detection range), a crude one only once close. ELINT is
///         deaf entirely while its own grid runs any active emitter (radar or jammer).
///     It subclasses <see cref="KsEmissionListenerSystem"/> for the shared listener
///         shape and, through it, the occluder line of sight run FROM the emitter: a
///         radar beam is blocked by terrain, so an ELINT in a radar's shadow is deaf to
///         it. Jamming ignores line of sight, so the jammer test does not.
/// </summary>
public sealed partial class KsElintSystem : KsEmissionListenerSystem
{
    [Dependency] private KsSensorSystem _sensors = default!;
    [Dependency] private KsSensorIntelSystem _intel = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsElintComponent, KsSensorSweepEvent>(OnSweep);
    }

    /// <summary>
    ///     Points every ELINT array on the grid at one designated emitter for focus
    ///         analysis (null clears): one focused emitter per grid, mirroring the
    ///         grid-wide radar/jammer toggles. Retargeting resets progress.
    /// </summary>
    public void SetGridFocus(EntityUid grid, EntityUid? target)
    {
        var query = EntityQueryEnumerator<KsElintComponent, TransformComponent>();
        while (query.MoveNext(out _, out var elint, out var xform))
        {
            if (xform.GridUid != grid || elint.FocusTarget == target)
                continue;

            elint.FocusTarget = target;
            elint.FocusProgress = 0f;
        }

        // Focus state lives on the sensor components, not in any contact pool, so
        // the change-gated console push must be forced for the FOCUSED flag to move.
        _sensors.ForceConsolePush();
    }

    private void OnSweep(Entity<KsElintComponent> ent, ref KsSensorSweepEvent args)
    {
        var xform = Transform(args.Sensor);
        if (xform.MapID == MapId.Nullspace || xform.GridUid is not { } ownGrid)
            return;

        // Self-blind: any active emitter (radar OR jammer) on our own grid drowns the ELINT.
        if (_sensors.IsGridEmitting(ownGrid))
            return;

        if (!GridQuery.TryGetComponent(ownGrid, out var ownGridComp)
            || !PhysicsQuery.TryGetComponent(ownGrid, out var ownPhysics))
            return;

        var mapId = xform.MapID;
        var (ownPos, ownRot) = XformSystem.GetWorldPositionRotation(ownGrid);
        var ownCom = ownPos + ownRot.RotateVec(ownPhysics.LocalCenter);
        var sensorPos = XformSystem.GetWorldPosition(xform);
        var ignore = Math.Clamp(ent.Comp.IgnoreFraction, 0f, 0.99f);
        var focusHeard = false;

        // Radar emitters. Heard when our grid is within the cone reach scaled by
        // sensitivity AND has a clear line of sight from the emitter (a radar beam is
        // occluded by terrain, so an ELINT in the emitter's shadow is deaf to it).
        foreach (var radar in _sensors.RadarEmissions)
        {
            if (radar.MapId != mapId || radar.Grid == ownGrid)
                continue;

            var effReach = radar.ConeReach * (1f - ignore);
            if (!IsAnyPartVisible(mapId, radar.Pos, effReach, ownGrid, ownGridComp.LocalAABB, ownPos, ownRot))
                continue;

            // The emitter's own detection range measures the emitting sensor rather than
            // the target grid, so the grid-metric evaluator cannot produce it; it still
            // goes through the prototype's formatting and the sensor's declared intel list.
            Dictionary<ProtoId<KsSensorIntelPrototype>, string>? intel = null;
            if (_intel.FormatDeclaredMetric(args.Sensor.Comp.Intel, KsSensorMetric.EmitterRange, radar.MaxRange) is { } readout)
                intel = new Dictionary<ProtoId<KsSensorIntelPrototype>, string> { [readout.Id] = readout.Value };

            focusHeard |= radar.Grid == ent.Comp.FocusTarget;
            args.Detections.Add(BuildEmitterDetection(radar.Grid, intel, typeOverride: null,
                radar.Band, radar.Pattern, SignalStrength(sensorPos, radar.Pos, radar.ConeReach)));
        }

        // Jammer emitters. Heard when our grid's centre of mass is inside the jam slice
        // scaled by sensitivity, with NO line of sight test: jamming is a loud broadband
        // emission that penetrates terrain, so ELINT hears a jammer through walls (a
        // deliberate asymmetry to radar). Classified as a jammer return, not a radar one.
        foreach (var jammer in _sensors.JammerEmissions)
        {
            if (jammer.MapId != mapId || jammer.Grid == ownGrid)
                continue;

            var effReach = jammer.Power * (1f - ignore);
            if (!jammer.Contains(ownCom, effReach))
                continue;

            focusHeard |= jammer.Grid == ent.Comp.FocusTarget;
            args.Detections.Add(BuildEmitterDetection(jammer.Grid, intel: null, typeOverride: KsSensorType.Jammer,
                jammer.Band, jammer.Pattern, SignalStrength(sensorPos, jammer.Pos, jammer.Power)));
        }

        if (focusHeard)
            AdvanceFocus(ent, args.Detections);
    }

    /// <summary>
    ///     One tick of focus analysis on the designated emitter this array heard this
    ///         sweep: progress advances one sweep's worth, every unlocked stage's
    ///         intel is evaluated against the target grid and folded into the
    ///         detections (sticky, so it survives track loss), and at 100% the
    ///         detections upgrade to an Exact fix for as long as the track is maintained.
    /// </summary>
    private void AdvanceFocus(Entity<KsElintComponent> ent, List<KsSensorDetection> detections)
    {
        var comp = ent.Comp;
        if (comp.FocusTarget is not { } focus)
            return;

        var step = (float) (_sensors.UpdateInterval.TotalSeconds / Math.Max(1f, comp.AnalysisTime));
        var advanced = Math.Min(1f, comp.FocusProgress + step);

        // Progress lives on this component, not in any contact pool, so the
        // change-gated console push must be forced for the ANALYSIS % to count up.
        // Only while it actually moves: a completed analysis held on a live track
        // would otherwise re-push every console every tick forever.
        if (advanced != comp.FocusProgress)
        {
            comp.FocusProgress = advanced;
            _sensors.ForceConsolePush();
        }

        Dictionary<ProtoId<KsSensorIntelPrototype>, string>? stageIntel = null;
        if (comp.AnalysisStages.Count > 0
            && PhysicsQuery.TryGetComponent(focus, out var physics)
            && GridQuery.TryGetComponent(focus, out var grid))
        {
            List<ProtoId<KsSensorIntelPrototype>>? unlocked = null;
            foreach (var stage in comp.AnalysisStages)
            {
                if (comp.FocusProgress < stage.Progress)
                    continue;

                unlocked ??= new List<ProtoId<KsSensorIntelPrototype>>();
                unlocked.AddRange(stage.Unlocks);
            }

            if (unlocked != null)
                stageIntel = _intel.Evaluate(unlocked, focus, physics, grid);
        }

        var exact = comp.FocusProgress >= 1f;
        if (stageIntel == null && !exact)
            return;

        for (var i = 0; i < detections.Count; i++)
        {
            var detection = detections[i];
            if (detection.TargetGrid != focus)
                continue;

            var intel = detection.Intel;
            if (stageIntel != null && intel == null)
            {
                intel = stageIntel;
            }
            else if (stageIntel != null)
            {
                // Copy-and-merge: source records treat detection intel as immutable.
                intel = new Dictionary<ProtoId<KsSensorIntelPrototype>, string>(intel!);
                foreach (var (key, value) in stageIntel)
                    intel[key] = value;
            }

            detections[i] = detection with
            {
                Intel = intel,
                QualityOverride = exact ? KsPositionQuality.Exact : detection.QualityOverride,
            };
        }
    }

}
