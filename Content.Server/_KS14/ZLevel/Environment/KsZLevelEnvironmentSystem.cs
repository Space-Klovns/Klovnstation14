using Content.Server.Atmos.EntitySystems;
using Content.Server.Light.EntitySystems;
using Content.Server.Weather;
using Content.Shared._KS14.ZLevel;
using Content.Shared.Atmos.Components;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Parallax;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Weather;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.ZLevel.Environment;

/// <summary>
///     Creates z-levels that carry the environment of another map: its air, gravity, lighting and day cycle, parallax and
///         weather, and keeps a <see cref="KsZLevelEnvironmentFollowerComponent"/> on them so that whatever changes the
///         source map later can find and change them too.
/// </summary>
public sealed partial class KsZLevelEnvironmentSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private LightCycleSystem _lightCycleSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private StatusEffectsSystem _statusEffectsSystem = default!;
    [Dependency] private WeatherSystem _weatherSystem = default!;

    [Dependency] private EntityQuery<KsZLevelEnvironmentFollowerComponent> _followerQuery = default!;

    private readonly List<EntityUid> _followerUids = [];

    /// <summary>
    ///     Creates a new map directly above <paramref name="zLevelEntity"/> in its stack, with that map's environment.
    /// </summary>
    public EntityUid CreateZLevelAbove(Entity<KsZLevelComponent> zLevelEntity)
    {
        var newMapUid = _mapSystem.CreateMap(out _, runMapInit: true);
        var newZLevelComponent = EnsureComp<KsZLevelComponent>(newMapUid);
        _zLevelSystem.AddZLevelDirectlyAbove(zLevelEntity.AsNullable(), (newMapUid, newZLevelComponent));

        CopyEnvironment(zLevelEntity.Owner, newMapUid);
        return newMapUid;
    }

    /// <summary>
    ///     The map <paramref name="mapUid"/> takes its environment from: its source if it follows one, else itself.
    /// </summary>
    public EntityUid GetEnvironmentSource(EntityUid mapUid)
    {
        return _followerQuery.TryComp(mapUid, out var followerComponent) && followerComponent.Source is { } sourceUid && Exists(sourceUid)
            ? sourceUid
            : mapUid;
    }

    /// <summary>
    ///     Adds every map following <paramref name="sourceMapUid"/>'s environment to <paramref name="followerUids"/>.
    /// </summary>
    public void GetFollowers(EntityUid sourceMapUid, List<EntityUid> followerUids)
    {
        var query = AllEntityQuery<KsZLevelEnvironmentFollowerComponent>();
        while (query.MoveNext(out var followerUid, out var followerComponent))
        {
            if (followerComponent.Source == sourceMapUid)
                followerUids.Add(followerUid);
        }
    }

    // Weather is a status effect on the map, so whatever changes it (a weather rule, the admin command) goes through
    //      these, raised on the weather's effect entity with the map as Target. A follower's own changes come back
    //      through them too; it follows no one and nothing follows it, so they stop there.

    [SubscribeLocalEvent]
    private void OnWeatherApplied(Entity<WeatherStatusEffectComponent> entity, ref StatusEffectAppliedEvent args)
    {
        MirrorWeather(entity.Owner, args.Target);
    }

    [SubscribeLocalEvent]
    private void OnWeatherEndTimeUpdated(Entity<WeatherStatusEffectComponent> entity, ref StatusEffectEndTimeUpdatedEvent args)
    {
        MirrorWeather(entity.Owner, args.Target);
    }

    [SubscribeLocalEvent]
    private void OnWeatherRemoved(Entity<WeatherStatusEffectComponent> entity, ref StatusEffectRemovedEvent args)
    {
        if (Prototype(entity.Owner) is not { } weatherPrototype || !CollectFollowers(args.Target))
            return;

        foreach (var followerUid in _followerUids)
            _statusEffectsSystem.TryRemoveStatusEffect(followerUid, weatherPrototype.ID);
    }

    /// <summary>
    ///     Puts the weather <paramref name="weatherUid"/> on every follower of <paramref name="mapUid"/>, ending when it
    ///         ends there.
    /// </summary>
    private void MirrorWeather(EntityUid weatherUid, EntityUid mapUid)
    {
        if (Prototype(weatherUid) is not { } weatherPrototype ||
            !TryComp<StatusEffectComponent>(weatherUid, out var statusEffectComponent) ||
            !CollectFollowers(mapUid))
            return;

        var duration = RemainingDuration(statusEffectComponent);
        foreach (var followerUid in _followerUids)
            _weatherSystem.TryAddWeather(followerUid, weatherPrototype.ID, out _, duration: duration);
    }

    /// <summary>
    ///     Fills <see cref="_followerUids"/> with <paramref name="mapUid"/>'s followers.
    /// </summary>
    /// <returns>Whether it has any.</returns>
    private bool CollectFollowers(EntityUid mapUid)
    {
        // Before clearing: a follower is reached while the list is being walked, mirroring onto it.
        if (_followerQuery.HasComp(mapUid))
            return false;

        _followerUids.Clear();
        GetFollowers(mapUid, _followerUids);
        return _followerUids.Count > 0;
    }

    private TimeSpan? RemainingDuration(StatusEffectComponent statusEffectComponent)
    {
        if (statusEffectComponent.EndEffectTime is not { } endTime)
            return null;

        // Never zero or less: that would read as "ends now" before the source's own weather has faded.
        var remaining = endTime - _gameTiming.CurTime;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromTicks(1);
    }

    /// <summary>
    ///     Copies <paramref name="fromMapUid"/>'s environment onto <paramref name="toMapUid"/>, and has the latter follow
    ///         the former's source from now on.
    /// </summary>
    public void CopyEnvironment(EntityUid fromMapUid, EntityUid toMapUid)
    {
        if (TryComp<MapAtmosphereComponent>(fromMapUid, out var atmosphereComponent))
            _atmosphereSystem.SetMapAtmosphere(toMapUid, atmosphereComponent.Space, atmosphereComponent.Mixture); // It clones a mutable one, and shares an immutable one.

        CopyIfPresent<GravityComponent>(fromMapUid, toMapUid);
        CopyIfPresent<ParallaxComponent>(fromMapUid, toMapUid);
        CopyIfPresent<SunShadowComponent>(fromMapUid, toMapUid);
        CopyIfPresent<SunShadowCycleComponent>(fromMapUid, toMapUid);

        if (TryComp<MapLightComponent>(fromMapUid, out var mapLightComponent))
            _mapSystem.SetAmbientLight(Comp<MapComponent>(toMapUid).MapId, mapLightComponent.AmbientLightColor);

        // After the light: the cycle reads its original colour from it. Added to an initialised map it rolls a random
        //      offset of its own, so it's set back to the source's, or the two would be at different times of day.
        if (TryComp<LightCycleComponent>(fromMapUid, out var lightCycleComponent))
        {
            var copiedComponent = CopyComp(fromMapUid, toMapUid, lightCycleComponent);
            _lightCycleSystem.SetOffset((toMapUid, copiedComponent), lightCycleComponent.Offset);
        }

        CopyWeather(fromMapUid, toMapUid);

        EnsureComp<KsZLevelEnvironmentFollowerComponent>(toMapUid).Source = GetEnvironmentSource(fromMapUid);
    }

    private void CopyIfPresent<T>(EntityUid fromMapUid, EntityUid toMapUid) where T : Component
    {
        if (TryComp<T>(fromMapUid, out var component))
            CopyComp(fromMapUid, toMapUid, component);
    }

    /// <summary>
    ///     Puts each weather on <paramref name="fromMapUid"/> on <paramref name="toMapUid"/> too, ending when it ends there.
    /// </summary>
    private void CopyWeather(EntityUid fromMapUid, EntityUid toMapUid)
    {
        if (!_statusEffectsSystem.TryEffectsWithComp<WeatherStatusEffectComponent>(fromMapUid, out var weatherEffects))
            return;

        foreach (var weatherEffect in weatherEffects)
        {
            if (Prototype(weatherEffect.Owner) is not { } weatherPrototype)
                continue;

            TimeSpan? duration = weatherEffect.Comp2.EndEffectTime is { } endTime
                ? endTime - _gameTiming.CurTime
                : null;

            if (duration is { } remaining && remaining <= TimeSpan.Zero)
                continue;

            _weatherSystem.TryAddWeather(toMapUid, weatherPrototype.ID, out _, duration: duration);
        }
    }
}
