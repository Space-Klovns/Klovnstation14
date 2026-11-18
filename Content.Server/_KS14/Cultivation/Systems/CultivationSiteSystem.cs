using System.Linq;
using Content.Server._KS14.Cultivation.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Tesla.Components;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Prototypes;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Singularity.Components;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Feng shui, station edition. On a cultivation mat, the things around a cultivator count towards the five elements:
///         their own law's element nourishes them, the element that generates it helps, and the one that overcomes it
///         suppresses them. Each element caps out, and identical things stop counting after two, so a hundred floor
///         tiles never beat a sacred mountain. Being alone helps too.
///     The station also has qi no mountain hermit ever had: a singularity, a ball of lightning, a fire. Meditating
///         beside one is far faster, and exactly as dangerous as it sounds.
/// </summary>
public sealed partial class CultivationSiteSystem : EntitySystem
{
    public const int ElementCap = 4;
    public const int SameKindCap = 2;
    public const float SiteRadius = 2.5f;
    public const float SeclusionRadius = 5f;

    /// <summary>
    ///     The generating cycle: each element feeds the next.
    /// </summary>
    public static readonly Dictionary<CultivationElement, CultivationElement> Generates = new()
    {
        [CultivationElement.Wood] = CultivationElement.Fire,
        [CultivationElement.Fire] = CultivationElement.Earth,
        [CultivationElement.Earth] = CultivationElement.Metal,
        [CultivationElement.Metal] = CultivationElement.Water,
        [CultivationElement.Water] = CultivationElement.Wood,
    };

    /// <summary>
    ///     The overcoming cycle: each element suppresses the one after it.
    /// </summary>
    public static readonly Dictionary<CultivationElement, CultivationElement> Overcomes = new()
    {
        [CultivationElement.Wood] = CultivationElement.Earth,
        [CultivationElement.Earth] = CultivationElement.Water,
        [CultivationElement.Water] = CultivationElement.Fire,
        [CultivationElement.Fire] = CultivationElement.Metal,
        [CultivationElement.Metal] = CultivationElement.Wood,
    };

    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private FlammableSystem _flammableSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;

    private readonly HashSet<EntityUid> _nearby = new();

    #region Feng shui

    /// <summary>
    ///     What a site gives one cultivator: how much faster they learn there, how much steadier a breakthrough is, and
    ///         why, in plain words, so players know what to change (and saboteurs leave evidence).
    /// </summary>
    public sealed class SiteReport
    {
        public readonly Dictionary<CultivationElement, int> ElementPoints = new();
        public float Multiplier = 1f;
        public int ReadinessBonus;
        public bool HasMat;
        public bool Secluded;
        public readonly List<string> Lines = new();
    }

    /// <summary>
    ///     Count element points around a point: each element capped, identical things counted twice at most.
    /// </summary>
    public Dictionary<CultivationElement, int> CountElements(EntityCoordinates centre)
    {
        var points = Enum.GetValues<CultivationElement>().ToDictionary(element => element, _ => 0);
        var kinds = new Dictionary<string, int>();
        var sources = _prototypeManager.EnumeratePrototypes<FengShuiElementPrototype>().ToList();

        _nearby.Clear();
        _entityLookupSystem.GetEntitiesInRange(centre, SiteRadius, _nearby);
        foreach (var uid in _nearby)
        {
            var kind = MetaData(uid).EntityPrototype?.ID ?? string.Empty;
            foreach (var source in sources)
            {
                if (points[source.Element] >= ElementCap || !Counts(uid, source))
                    continue;

                if (kinds.GetValueOrDefault(kind) >= SameKindCap)
                    break;

                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                points[source.Element]++;
                break;
            }
        }

        return points;
    }

    private bool Counts(EntityUid uid, FengShuiElementPrototype source)
    {
        if (source.Burning && TryComp<FlammableComponent>(uid, out var flammableComponent) && flammableComponent.OnFire)
            return true;

        if (source.Stacks.Count > 0 && TryComp<StackComponent>(uid, out var stackComponent) && source.Stacks.Contains(stackComponent.StackTypeId))
            return true;

        foreach (var componentName in source.Components)
        {
            if (_componentFactory.TryGetRegistration(componentName, out var registration) && HasComp(uid, registration.Type))
                return true;
        }

        if (MetaData(uid).EntityPrototype is not { } prototype)
            return false;

        foreach (var prefix in source.Prefixes)
        {
            if (prototype.ID.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        if (source.Prototypes.Count == 0)
            return false;

        if (source.Prototypes.Contains(prototype.ID))
            return true;

        foreach (var parent in _prototypeManager.EnumerateParents<EntityPrototype>(prototype.ID))
        {
            if (source.Prototypes.Contains(parent.ID))
                return true;
        }

        return false;
    }

    public SiteReport Evaluate(Entity<CultivatorComponent> entity)
    {
        var report = new SiteReport();
        var coordinates = Transform(entity).Coordinates;
        report.HasMat = OnMat(entity);
        foreach (var (element, points) in CountElements(coordinates))
            report.ElementPoints[element] = points;

        report.Secluded = Secluded(entity);

        var net = 0f;
        if (!report.HasMat)
        {
            report.Lines.Add(Loc.GetString("cultivation-site-no-mat"));
        }
        else if (entity.Comp.Laws.Count == 0)
        {
            report.Lines.Add(Loc.GetString("cultivation-site-no-law"));
        }
        else
        {
            foreach (var lawId in entity.Comp.Laws)
            {
                if (!_prototypeManager.TryIndex(lawId, out var law))
                    continue;

                var element = law.Element;
                var mother = Generates.First(pair => pair.Value == element).Key;
                var enemy = Overcomes.First(pair => pair.Value == element).Key;
                var own = report.ElementPoints[element];
                var support = report.ElementPoints[mother];
                var opposition = report.ElementPoints[enemy];
                var lawName = Loc.GetString(law.Name);
                var elementName = ElementName(element);

                report.Lines.Add(own >= 3
                    ? Loc.GetString("cultivation-site-element-strong", ("element", elementName), ("law", lawName))
                    : own > 0
                        ? Loc.GetString("cultivation-site-element-some", ("element", elementName), ("law", lawName))
                        : Loc.GetString("cultivation-site-element-none", ("element", elementName), ("law", lawName)));
                if (support > 0)
                    report.Lines.Add(Loc.GetString("cultivation-site-element-fed", ("mother", ElementName(mother)), ("element", elementName)));

                if (opposition >= 2)
                    report.Lines.Add(Loc.GetString("cultivation-site-element-suppressed", ("enemy", ElementName(enemy)), ("element", elementName)));

                net += own + support * 0.5f - opposition;
            }
        }

        report.Lines.Add(Loc.GetString(report.Secluded ? "cultivation-site-secluded" : "cultivation-site-crowded"));
        report.Multiplier = 1f + (report.HasMat ? 0.1f : 0f) + Math.Clamp(net * 0.05f, -0.2f, 0.5f) + (report.Secluded ? 0.25f : 0f);
        report.ReadinessBonus = (report.HasMat ? 10 : 0) + Math.Clamp((int)MathF.Round(net * 3f), -20, 25) + (report.Secluded ? 5 : 0);
        return report;
    }

    /// <summary>
    ///     Whether no other living player is close by.
    /// </summary>
    public bool Secluded(EntityUid uid)
    {
        _nearby.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(uid).Coordinates, SeclusionRadius, _nearby);
        foreach (var otherUid in _nearby)
        {
            if (otherUid != uid && HasComp<ActorComponent>(otherUid) && HasComp<MobStateComponent>(otherUid) && !_mobStateSystem.IsDead(otherUid))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     What a place gives a body cultivator's Tribulation of Flesh. The flesh doesn't care for elements, only for a
    ///         meditation mat to kneel on and nobody close by.
    /// </summary>
    public int BodyReadinessBonus(EntityUid uid)
    {
        return (OnMat(uid) ? 10 : 0) + (Secluded(uid) ? 5 : 0);
    }

    public string ElementName(CultivationElement element)
    {
        return Loc.GetString($"cultivation-element-{element}");
    }

    /// <summary>
    ///     Is there a mat laid on the tile they're on?
    /// </summary>
    public bool OnMat(EntityUid uid)
    {
        var coordinates = Transform(uid).Coordinates;
        if (_transformSystem.GetGrid(coordinates) is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var gridComponent))
            return false;

        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, gridComponent, _mapSystem.TileIndicesFor(gridUid, gridComponent, coordinates));
        while (anchored.MoveNext(out var anchoredUid))
        {
            if (HasComp<CultivationMatComponent>(anchoredUid))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     A cultivator reads the five elements flowing round a mat.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMatExamined(Entity<CultivationMatComponent> entity, ref ExaminedEvent args)
    {
        if (!HasComp<CultivatorComponent>(args.Examiner))
        {
            args.PushMarkup(Loc.GetString("cultivation-mat-mortal"));
            return;
        }

        var points = CountElements(Transform(entity).Coordinates);
        var readout = string.Join(", ", points.Select(pair => $"{ElementName(pair.Key)} {pair.Value}/{ElementCap}"));
        args.PushMarkup(Loc.GetString("cultivation-mat-reading", ("readout", readout)));
    }

    #endregion

    #region Station qi

    /// <summary>
    ///     Meditating beside the station's wild qi: returns the extra multiplier this cycle (0 with none nearby), and does
    ///         the harm. A singularity's qi comes with radiation, a ball of lightning's with shocks, a fire's with burns.
    /// </summary>
    public float StationQiCycle(Entity<CultivatorComponent> entity)
    {
        var coordinates = Transform(entity).Coordinates;
        var extra = 0f;
        var lines = new List<string>();

        _nearby.Clear();
        _entityLookupSystem.GetEntitiesInRange(coordinates, 6f, _nearby);
        if (_nearby.Any(HasComp<SingularityComponent>))
        {
            extra += 2f;
            _sharedCultivationSystem.AddInsight(entity.AsNullable(), 10f);
            _damageableSystem.TryChangeDamage(entity.Owner, CultivationSystem.Damage("Radiation", 4f), interruptsDoAfters: false);
            lines.Add(Loc.GetString("cultivation-station-qi-singularity-feed"));
        }

        if (_nearby.Any(HasComp<TeslaEnergyBallComponent>))
        {
            extra += 2f;
            _sharedCultivationSystem.AddInsight(entity.AsNullable(), 10f);
            if (_robustRandom.Prob(0.4f))
                _damageableSystem.TryChangeDamage(entity.Owner, CultivationSystem.Damage("Shock", 15f), interruptsDoAfters: false);

            lines.Add(Loc.GetString("cultivation-station-qi-lightning-feed"));
        }

        if (NearFire(coordinates))
        {
            extra += 1f;
            _sharedCultivationSystem.AddInsight(entity.AsNullable(), 6f);
            _damageableSystem.TryChangeDamage(entity.Owner, CultivationSystem.Damage("Heat", 5f), interruptsDoAfters: false);
            if (TryComp<FlammableComponent>(entity, out var flammableComponent))
                _flammableSystem.AdjustFireStacks(entity, 1f, flammableComponent, ignite: true);

            lines.Add(Loc.GetString("cultivation-station-qi-fire-feed"));
        }

        foreach (var line in lines)
            _noticeSystem.Notify(entity, line, Shared.Popups.PopupType.MediumCaution);

        if (extra > 0f)
            _visualsSystem.Wave(entity, FxColour.Gold);

        return extra;
    }

    /// <summary>
    ///     What Spiritual Sense notices about the station's wild qi nearby.
    /// </summary>
    public List<string> StationQiLines(EntityUid uid)
    {
        var coordinates = Transform(uid).Coordinates;
        var lines = new List<string>();
        _nearby.Clear();
        _entityLookupSystem.GetEntitiesInRange(coordinates, 6f, _nearby);
        if (_nearby.Any(HasComp<SingularityComponent>))
            lines.Add(Loc.GetString("cultivation-station-qi-singularity"));

        if (_nearby.Any(HasComp<TeslaEnergyBallComponent>))
            lines.Add(Loc.GetString("cultivation-station-qi-lightning"));

        if (NearFire(coordinates))
            lines.Add(Loc.GetString("cultivation-station-qi-fire"));

        return lines;
    }

    private bool NearFire(EntityCoordinates coordinates)
    {
        if (_transformSystem.GetGrid(coordinates) is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var gridComponent))
            return false;

        var centre = _mapSystem.TileIndicesFor(gridUid, gridComponent, coordinates);
        for (var x = -2; x <= 2; x++)
        {
            for (var y = -2; y <= 2; y++)
            {
                if (_atmosphereSystem.IsHotspotActive(gridUid, centre + new Vector2i(x, y)))
                    return true;
            }
        }

        return false;
    }

    #endregion
}
