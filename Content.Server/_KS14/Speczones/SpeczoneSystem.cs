// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Speczones;
using Content.Shared.GameTicking;
using Content.Shared.Random.Helpers;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Server._KS14.Speczones;

/// <inheritdoc/>
public sealed partial class SpeczoneSystem : SharedSpeczoneSystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoaderSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;

    private EntityQuery<SpeczoneComponent> _speczoneQuery;

    /// <summary>
    ///     Dictionary of loaded speczones cached by their prototype ID.
    ///         Don't modify this directly.
    /// </summary>
    private readonly Dictionary<string, Entity<SpeczoneComponent>> _speczones = new();

    /// <summary>
    ///     Loaded speczones cached by their EntityUid.
    ///         Don't modify this directly.
    /// </summary>
    private readonly HashSet<EntityUid> _speczoneUids = new();

    /// <summary>
    ///     Reverse backing field for <see cref="KsCCVars"/>.
    /// </summary>
    private bool _loadSpeczones = true;

    public override void Initialize()
    {
        base.Initialize();

        _speczoneQuery = GetEntityQuery<SpeczoneComponent>();

        _configurationManager.OnValueChanged(KsCCVars.SpeczonesEnabled, x => _loadSpeczones = x, invokeImmediately: true);

        SubscribeLocalEvent<SpeczoneComponent, ComponentShutdown>(OnSpeczoneShutdown);
        SubscribeLocalEvent<SpeczoneEntryComponent, ComponentShutdown>(OnSpeczoneEntryShutdown);

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        SetupRelocation();
    }

    protected override bool HasSpeczoneComponent(EntityUid uid) => HasComp<SpeczoneComponent>(uid);

    private void OnSpeczoneShutdown(Entity<SpeczoneComponent> entity, ref ComponentShutdown args)
    {
        _speczones.Remove(entity.Comp.PrototypeId);
        _speczoneUids.Remove(entity.Owner);
    }

    private void OnSpeczoneEntryShutdown(Entity<SpeczoneEntryComponent> entity, ref ComponentShutdown args)
    {
        var entityTransform = Transform(entity.Owner);
        if (entityTransform.MapUid is not { } mapUid ||
            !_speczoneQuery.TryGetComponent(mapUid, out var mapSpeczoneComponent))
            return;

        mapSpeczoneComponent.EntryMarkers.Remove((entity.Owner, entityTransform));
    }

    private void OnRoundStarting(RoundStartingEvent args)
    {
        if (!_loadSpeczones)
            return;
        // Initialise speczones

        foreach (var speczonePrototype in _prototypeManager.EnumeratePrototypes<SpeczonePrototype>())
            TryLoadSpeczonePrototype(speczonePrototype, out _);

        UpdateSpeczoneEntryPoints();
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _speczones.Clear();
        _speczoneUids.Clear();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.TryGetModified<SpeczonePrototype>(out var modifiedZones))
            return;

        var anythingHappenedEver = false; // nothing ever happens
        foreach (var modifiedZone in modifiedZones)
        {
            // don't care about already-existing speczones
            if (_speczones.ContainsKey(modifiedZone))
                continue;

            // load a new speczone
            if (_prototypeManager.TryIndex<SpeczonePrototype>(modifiedZone, out var speczonePrototype))
            {
                TryLoadSpeczonePrototype(speczonePrototype, out _);
                anythingHappenedEver = true;
            }
        }

        if (anythingHappenedEver)
            UpdateSpeczoneEntryPoints();
    }

    /// <summary>
    ///     Loads a map for a speczone. Does not initialise <see cref="SpeczoneEntryComponent"/>s on the
    ///         <see cref="SpeczoneComponent"/>, and does not process invincibility.
    /// </summary>
    /// <returns>False if no map was loaded successfully.</returns>
    private bool TryLoadSpeczonePrototype(SpeczonePrototype prototype, [NotNullWhen(true)] out Entity<SpeczoneComponent>? speczoneEntity)
    {
        if (_speczones.ContainsKey(prototype.ID))
        {
            DebugTools.Assert($"When loading speczone prototype of ID {prototype.ID}, it already existed in internal cache.");
            Log.Error($"When loading speczone prototype of ID {prototype.ID}, it already existed in internal cache.");

            speczoneEntity = null;
            return false;
        }

        if (!_mapLoaderSystem.TryLoadMap(
            prototype.MapPath,
            out var mapEntity,
            out var loadedGrids,
            DeserializationOptions.Default with { InitializeMaps = true, PauseMaps = true })
        )
        {
            DebugTools.Assert($"When loading speczone prototype of ID {prototype.ID}, failed to load map!");
            Log.Error($"When loading speczone prototype of ID {prototype.ID}, failed to load map!");

            speczoneEntity = null;
            return false;
        }

        // adding the speczone to the internal cache is handled by OnSpeczoneStartup
        var speczoneComponent = _componentFactory.GetComponent<SpeczoneComponent>();
        speczoneComponent.PrototypeId = prototype.ID;
        AddComp(mapEntity.Value.Owner, speczoneComponent, overwrite: false);

        // this one method will probably take a while
        StartInvincibilityProcessingHierarchy(loadedGrids);

        speczoneEntity = (mapEntity.Value.Owner, speczoneComponent);

        _speczones[prototype.ID] = speczoneEntity.Value;
        _speczoneUids.Add(mapEntity.Value.Owner);

        return true;
    }

    /// <summary>
    ///     Adds new speczone entry-points to their current map's <see cref="SpeczoneComponent.EntryMarkers"/>.
    ///         Does not remove any. Only adds. 
    /// </summary>
    private void UpdateSpeczoneEntryPoints()
    {
        // include paused
        var speczoneEntryEnumerator = EntityManager.AllEntityQueryEnumerator<SpeczoneEntryComponent, TransformComponent>();
        while (speczoneEntryEnumerator.MoveNext(out var uid, out var _, out var transformComponent))
        {
            if (!_speczoneQuery.TryGetComponent(transformComponent.MapUid, out var speczoneComponent))
            {
                Log.Info($"When updating speczone entry points, '{ToPrettyString(uid)}' was not on a speczone map. Map: '{ToPrettyString(transformComponent.MapUid) ?? "N/A"}'");
                continue;
            }

            speczoneComponent.EntryMarkers.Add((uid, transformComponent));
        }
    }

    /// <summary>
    ///     Tries to get a random entry point of a speczone.
    /// </summary>
    /// <returns>True if one was found.</returns>
    [Pure]
    public bool TryGetSpeczoneEntryPoint(Entity<SpeczoneComponent?> speczoneEntity, [NotNullWhen(true)] out EntityCoordinates? entryCoordinates)
    {
        if (!_speczoneQuery.Resolve(speczoneEntity.Owner, ref speczoneEntity.Comp) ||
            speczoneEntity.Comp.EntryMarkers.Count == 0)
        {
            entryCoordinates = null;
            return false;
        }

        var entryPoint = _robustRandom.Pick(speczoneEntity.Comp.EntryMarkers);
        entryCoordinates = entryPoint.Comp.Coordinates;

        return true;
    }

    /// <summary>
    ///     Tries to insert an entity into a speczone.
    ///         Specified speczone defaults to first one available
    ///         if none was specified. Does nothing if there are no
    ///         entry points to the speczone.
    /// 
    ///     Unpauses the speczone if necessary.
    /// </summary>
    /// <returns>True if the entity was moved.</returns>
    public bool TryInsertIntoSpeczone(Entity<TransformComponent?> entity, string? speczoneId, [NotNullWhen(true)] out EntityCoordinates? entryCoordinates)
    {
        if (_speczones.Count == 0 ||
            !EntityManager.TransformQuery.Resolve(entity.Owner, ref entity.Comp))
        {
            entryCoordinates = null;
            return false;
        }

        speczoneId ??= _speczones.Keys.First();
        if (!_speczones.TryGetValue(speczoneId, out var speczoneEntity))
        {
            entryCoordinates = null;
            return false;
        }

        if (!TryGetSpeczoneEntryPoint(speczoneEntity!, out entryCoordinates))
            return false;

        _mapSystem.SetPaused(speczoneEntity.Owner, false);
        _transformSystem.SetCoordinates(entity.Owner, entity.Comp, entryCoordinates.Value, unanchor: true);
        return true;
    }

    /// <summary>
    ///     Tries to get a speczone given it's prototype ID.
    /// </summary>
    /// <param name="id">Prototype ID of the speczone being queried for.</param>
    /// <param name="speczoneEntity">Null when this method returns false.</param>
    /// <returns>True if the specified speczone was found, false otherwise.</returns>
    public bool TryGetSpeczoneEntity(string id, [MaybeNullWhen(false)] out Entity<SpeczoneComponent> speczoneEntity)
        => _speczones.TryGetValue(id, out speczoneEntity);

    /// <returns>An enumerator of the <see cref="EntityUid"/>s of every existing speczone.</returns>
    public IEnumerator<EntityUid> GetSpeczoneUidEnumerator()
        => _speczoneUids.GetEnumerator();
}
