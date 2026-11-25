using Content.Server.Pinpointer;
using Content.Server.Station.Systems;
using Content.Shared.Pinpointer;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     In the wuxia gamemodes heaven and earth's qi wells up at one place on the station at a time: a Sacred Site, at a
///         random nav beacon, moving every ten minutes. The whole station is told where. Meditating or forging the body
///         within three tiles of it goes twice as fast, so it's worth holding, and worth fighting for.
/// </summary>
public sealed partial class SacredSiteSystem : EntitySystem
{
    private static readonly EntProtoId SitePrototype = "KsSacredSite";
    private static readonly TimeSpan MoveInterval = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     How long into the round before the first site appears, so people have their bearings first.
    /// </summary>
    private static readonly TimeSpan FirstSiteDelay = TimeSpan.FromMinutes(3);

    public const float Range = 3f;
    public const float SpeedMultiplier = 2f;

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private NavMapSystem _navMapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private WuxiaWorldRuleSystem _wuxiaWorldSystem = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        var siteQuery = EntityQueryEnumerator<SacredSiteComponent>();
        var hasSite = false;
        while (siteQuery.MoveNext(out var siteUid, out var siteComponent))
        {
            hasSite = true;
            if (now < siteComponent.MovesAt)
                continue;

            // Time to move on: heaven and earth's qi wells up somewhere else.
            QueueDel(siteUid);
            if (_wuxiaWorldSystem.WuxiaActive())
                TrySpawnSite(now);
        }

        if (hasSite || !_wuxiaWorldSystem.WuxiaActive())
            return;

        // The first site waits a few minutes into the round.
        if (_nextFirstSite is not { } firstAt)
        {
            _nextFirstSite = now + FirstSiteDelay;
            return;
        }

        if (now >= firstAt)
        {
            _nextFirstSite = null;
            TrySpawnSite(now);
        }
    }

    private TimeSpan? _nextFirstSite;

    [SubscribeLocalEvent]
    private void OnRoundRestart(Content.Shared.GameTicking.RoundRestartCleanupEvent args)
    {
        _nextFirstSite = null;
    }

    private void TrySpawnSite(TimeSpan now)
    {
        var candidates = new List<EntityUid>();
        var beaconQuery = EntityQueryEnumerator<NavMapBeaconComponent, TransformComponent>();
        while (beaconQuery.MoveNext(out var beaconUid, out var beacon, out var xform))
        {
            if (beacon.Enabled && xform.Anchored && xform.GridUid != null && _stationSystem.GetOwningStation(beaconUid, xform) != null)
                candidates.Add(beaconUid);
        }

        if (candidates.Count == 0)
            return;

        var chosen = _robustRandom.Pick(candidates);
        var siteUid = Spawn(SitePrototype, Transform(chosen).Coordinates);
        var siteComponent = EnsureComp<SacredSiteComponent>(siteUid);
        siteComponent.MovesAt = now + MoveInterval;
        siteComponent.Name = _navMapSystem.GetNearestBeaconString(chosen, onlyName: true);

        _noticeSystem.Proclaim(null, Loc.GetString("cultivation-sacred-site-appears", ("place", siteComponent.Name)),
            Loc.GetString("cultivation-heavenly-omen"), colorOverride: Color.Gold);
    }

    /// <summary>
    ///     How much faster meditation and forging go here: twice as fast within three tiles of the Sacred Site.
    /// </summary>
    public float Multiplier(EntityUid uid)
    {
        var here = _transformSystem.GetMapCoordinates(uid);
        var siteQuery = EntityQueryEnumerator<SacredSiteComponent, TransformComponent>();
        while (siteQuery.MoveNext(out var siteUid, out _, out var siteXform))
        {
            var there = _transformSystem.GetMapCoordinates(siteUid, siteXform);
            if (here.MapId == there.MapId && (here.Position - there.Position).Length() <= Range)
                return SpeedMultiplier;
        }

        return 1f;
    }

    /// <summary>
    ///     Where the Sacred Site is now, or null if there isn't one.
    /// </summary>
    public string? CurrentPlace()
    {
        var siteQuery = EntityQueryEnumerator<SacredSiteComponent>();
        return siteQuery.MoveNext(out _, out var siteComponent) ? siteComponent.Name : null;
    }
}

/// <summary>
///     Where heaven and earth's qi wells up for now.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(SacredSiteSystem))]
public sealed partial class SacredSiteComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan MovesAt;

    [DataField]
    public string Name = string.Empty;
}
