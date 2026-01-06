// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using Content.Shared._KS14.Speczones;
using Content.Shared.Doors.Components;
using Content.Shared.GameTicking;
using Content.Shared.Random.Helpers;
using Content.Shared.RCD.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Speczones;

/// <inheritdoc/>
public sealed partial class SpeczoneSystem : SharedSpeczoneSystem
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoaderSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;

    private EntityQuery<SpeczoneComponent> _speczoneQuery;
    private EntityQuery<RCDDeconstructableComponent> _rcdDeconstructableQuery;
    private EntityQuery<DoorComponent> _doorQuery;

    /// <summary>
    ///     Dictionary of speczones cached by their prototype ID.
    ///         Don't modify this directly.
    /// </summary>
    private readonly Dictionary<string, Entity<SpeczoneComponent>> _speczones = new();

    /// <summary>
    ///     Speczones cached by their EntityUid.
    ///         Don't modify this directly.
    /// </summary>
    private readonly HashSet<EntityUid> _speczoneUids = new();

    public override void Initialize()
    {
        base.Initialize();

        _speczoneQuery = GetEntityQuery<SpeczoneComponent>();
        _rcdDeconstructableQuery = GetEntityQuery<RCDDeconstructableComponent>();
        _doorQuery = GetEntityQuery<DoorComponent>();

        SubscribeLocalEvent<SpeczoneComponent, ComponentStartup>(OnSpeczoneStartup);
        SubscribeLocalEvent<SpeczoneComponent, ComponentShutdown>(OnSpeczoneShutdown);
        SubscribeLocalEvent<SpeczoneEntryComponent, ComponentShutdown>(OnSpeczoneEntryShutdown);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        foreach (var speczonePrototype in _prototypeManager.EnumeratePrototypes<SpeczonePrototype>())
            TryLoadSpeczonePrototype(speczonePrototype, out _);

        UpdateSpeczoneEntryPoints();
    }

    protected override bool HasSpeczoneComponent(EntityUid uid) => HasComp<SpeczoneComponent>(uid);

    private void OnSpeczoneStartup(Entity<SpeczoneComponent> entity, ref ComponentStartup args)
    {
        if (_speczones.ContainsKey(entity.Comp.Prototype.ID))
        {
            DebugTools.Assert($"While SpeczoneComponent was starting, speczone of same ID {entity.Comp.Prototype.ID} already existed in cache!");
            Log.Error($"While SpeczoneComponent was starting, speczone of same ID {entity.Comp.Prototype.ID} already existed in cache!"
                + "SpeczoneComponent of existing speczone will be removed.");
            RemCompDeferred(entity.Owner, entity.Comp);
            return;
        }

        _speczones[entity.Comp.Prototype.ID] = entity;
        _speczoneUids.Add(entity.Owner);

        UpdateSpeczoneEntryPoints();
    }

    private void OnSpeczoneShutdown(Entity<SpeczoneComponent> entity, ref ComponentShutdown args)
    {
        _speczones.Remove(entity.Comp.Prototype.ID);
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
    ///     Loads a map for a speczone. Does not initialise <see cref="SpeczoneEntryComponent"/> on the
    ///         <see cref="SpeczoneComponent"/>.
    /// </summary>
    /// <returns>False if no map was loaded successfully.</returns>
    private bool TryLoadSpeczonePrototype(SpeczonePrototype prototype, [NotNullWhen(true)] out Entity<SpeczoneComponent>? speczoneEntity)
    {
        if (_speczones.ContainsKey(prototype.ID))
        {
            speczoneEntity = null;
            return false;
        }

        if (!_mapLoaderSystem.TryLoadMap(
            prototype.MapPath,
            out var maybeMapEntity,
            out var _,
            DeserializationOptions.Default with { InitializeMaps = true, PauseMaps = true }) ||
            maybeMapEntity is not { } mapEntity)
        {
            speczoneEntity = null;
            return false;
        }

        // adding the speczone to the internal cache is handled by OnSpeczoneStartup
        var speczoneComponent = _componentFactory.GetComponent<SpeczoneComponent>();
        speczoneComponent.Prototype = prototype;
        AddComp(mapEntity.Owner, speczoneComponent, overwrite: false);

        ProcessSpeczoneInvincibility(prototype, mapEntity.Owner);
        speczoneEntity = (mapEntity.Owner, speczoneComponent);
        return true;
    }

    /// <summary>
    ///     Adds new speczone entry-points to their current map's <see cref="SpeczoneComponent.EntryMarkers"/>.
    ///         Does not remove any. Only adds. 
    /// </summary>
    private void UpdateSpeczoneEntryPoints()
    {
        var speczoneEntryEnumerator = EntityQueryEnumerator<SpeczoneEntryComponent, TransformComponent>();
        while (speczoneEntryEnumerator.MoveNext(out var uid, out var _, out var transformComponent))
        {
            if (!_speczoneQuery.TryGetComponent(transformComponent.MapUid, out var speczoneComponent))
            {
                Log.Error($"Speczone entry point '{ToPrettyString(uid)}' was not on a speczone map. Map: '{ToPrettyString(transformComponent.MapUid) ?? "N/A"}'");
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
    ///         if none was specified.
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
}
