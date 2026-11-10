using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Transit;
using Content.Shared.Atmos.Components;
using Content.Shared.Gravity;
using Content.Shared.Parallax;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._KS14.ZLevel.Transit;

/// <summary>
///     Creates and tears down the maps that sit between z-levels, and moves grids on and off them.
/// </summary>
/// <remarks>
///     Server-only because all of it is: making a map, copying an environment onto it and reparenting a whole
///         grid are none of them things a client may guess at. What the client does run is the progress
///         through the gap, which lives on the shared half.
/// </remarks>
public sealed partial class KsZLevelGapSystem : SharedKsZLevelGapSystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private ServerChunkEntitySystem _chunkEntitySystem = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private PvsOverrideSystem _pvsOverrideSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<MapComponent> _mapQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    private readonly List<Entity<KsZLevelGapComponent>> _orphanedGaps = [];

    // Copied out before anything is reparented, because reparenting is what mutates the child set.
    private readonly List<EntityUid> _strandedEntities = [];

    /// <summary>
    ///     Puts a grid on a newly made gap map between two z-levels.
    /// </summary>
    /// <param name="progress">Where in the gap the grid starts, 0 at the lower z-level and 1 at the upper.</param>
    /// <returns>Whether the grid is now crossing a gap.</returns>
    public bool TryEnterGap(
        EntityUid gridUid,
        Entity<KsZLevelComponent> lowerZLevelEntity,
        Entity<KsZLevelComponent> upperZLevelEntity,
        float progress,
        out Entity<KsZLevelGapComponent>? gapEntity)
    {
        gapEntity = null;

        if (TerminatingOrDeleted(gridUid) ||
            !_mapQuery.HasComponent(lowerZLevelEntity.Owner) ||
            !_mapQuery.HasComponent(upperZLevelEntity.Owner))
            return false;

        var gapUid = _mapSystem.CreateMap(out var gapMapId, runMapInit: true);
        _metaDataSystem.SetEntityName(gapUid, $"z-level gap above {Name(lowerZLevelEntity.Owner)}");

        CopyEnvironment(gapUid, upperZLevelEntity.Owner, lowerZLevelEntity.Owner);

        // The gap gets a z-level component of its own so that everything asking "what z-level is this entity
        //      on" keeps working for a rider. It stays in its own one-member stack - the navigation API
        //      answers stack questions about the anchor instead, which is what keeps floor numbering and the
        //      light-from-above pass looking at real floors. See KsZLevelGapComponent.
        EnsureComp<KsZLevelComponent>(gapUid);

        var gapComponent = AddComp<KsZLevelGapComponent>(gapUid);
        gapComponent.LowerZLevel = lowerZLevelEntity.Owner;
        gapComponent.UpperZLevel = upperZLevelEntity.Owner;
        gapComponent.TotalDepth = MathF.Max(lowerZLevelEntity.Comp.Depth, KsZLevelSystem.MinimumDepth);
        gapComponent.Progress = Math.Clamp(progress, 0f, 1f);
        gapComponent.PrimaryGrid = gridUid;
        Dirty(gapUid, gapComponent);

        if (!TryMoveGridToMap(gridUid, gapUid))
        {
            Del(gapUid);
            return false;
        }

        // A gap is on no z-level, so KsZLevelPvsSystem's "mirror the level below the player" never reaches
        //      it and nothing here would be sent to anyone not standing on it.
        // The override goes on the map rather than on the grid. PvsSystem walks an override's children
        //      recursively, so overriding the map covers the platform, everything riding it, and anything
        //      falling past it - all of which are children of the map, directly or otherwise. Overriding
        //      the grid instead covers only the grid's own subtree, which leaves a falling entity a sibling
        //      of the platform and so never transmitted at all.
        // Global rather than per-session, at the cost of sending a crossing to every client rather than to
        //      the ones who could see it. A gap holds a platform and lasts one leg, and the alternative is
        //      re-deciding who can see it every tick for the length of the ride.
        _pvsOverrideSystem.AddGlobalOverride(gapUid);

        // And the grid's chunk entities by hand, because the recursion above cannot reach them. A chunk
        //      entity is a nullspace entity tied to its grid by ChunkEntityComponent.Root rather than by the
        //      transform tree (Robust.Shared/GameStates/ChunkEntitySystem.cs), so a walk over children steps
        //      straight past it, and PvsSystem otherwise sends one only to viewers whose own PVS reaches the
        //      chunk - which is nobody looking in from another z-level.
        // Decals are what this is for in practice: they live on chunk entities, so without it a platform
        //      arrives with bare tiles for everyone not riding it, while its walls, emissives and stains -
        //      all ordinary children - come through fine.
        SetGridChunkOverrides(gridUid, overridden: true);

        // Sizes the slice of airspace this gap owns, which is what lets a fall through it be an ordinary
        //      fall rather than a special case.
        RebuildSliceDepths(lowerZLevelEntity.Owner);

        gapEntity = (gapUid, gapComponent);
        return true;
    }

    /// <summary>
    ///     Adds or removes the global PVS override on every chunk entity belonging to a grid.
    /// </summary>
    /// <seealso cref="TryEnterGap"/>
    private void SetGridChunkOverrides(EntityUid gridUid, bool overridden)
    {
        foreach (var chunkEntity in _chunkEntitySystem.GetChunks(gridUid))
        {
            if (overridden)
                _pvsOverrideSystem.AddGlobalOverride(chunkEntity.Owner);
            else
                _pvsOverrideSystem.RemoveGlobalOverride(chunkEntity.Owner);
        }
    }

    /// <summary>
    ///     Catches a chunk entity that comes into being while its root is already crossing a gap.
    /// </summary>
    /// <remarks>
    ///     A grid's chunks are made on demand, so a decal painted onto a platform mid-ride creates one that
    ///         missed the sweep in <see cref="TryEnterGap"/>. Without this it stays invisible from every
    ///         other z-level until the platform lands.
    ///     Nothing is needed for the reverse: a chunk entity is only ever removed by being deleted, and
    ///         PvsOverrideSystem drops overrides on deletion itself.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnChunkEntityAdded(ref ChunkEntityAddedEvent args)
    {
        if (Transform(args.Root).MapUid is not { } mapUid || !HasComp<KsZLevelGapComponent>(mapUid))
            return;

        _pvsOverrideSystem.AddGlobalOverride(args.Entity);
    }

    /// <summary>
    ///     Takes the grid off a gap and onto a z-level, and deletes the gap.
    /// </summary>
    /// <returns>Whether the grid landed.</returns>
    public bool TryLeaveGap(Entity<KsZLevelGapComponent> entity, Entity<KsZLevelComponent> destinationEntity)
    {
        if (entity.Comp.PrimaryGrid is not { } gridUid ||
            TerminatingOrDeleted(gridUid) ||
            !_mapQuery.HasComponent(destinationEntity.Owner))
        {
            DestroyGap(entity);
            return false;
        }

        var moved = TryMoveGridToMap(gridUid, destinationEntity.Owner);
        DestroyGap(entity);

        return moved;
    }

    /// <summary>
    ///     Deletes a gap map, and anything unlucky enough to still be on it.
    /// </summary>
    public void DestroyGap(Entity<KsZLevelGapComponent> entity)
    {
        if (TerminatingOrDeleted(entity.Owner))
            return;

        _pvsOverrideSystem.RemoveGlobalOverride(entity.Owner);

        // Taken off explicitly rather than left to the deletion hook, because the grid outlives the gap: it
        //      has landed by now and its chunks are back under ordinary PVS, so a leftover override would
        //      send every decal on it to every client for the rest of the round.
        if (entity.Comp.PrimaryGrid is { } gridUid && !TerminatingOrDeleted(gridUid))
            SetGridChunkOverrides(gridUid, overridden: false);

        EvacuateGap(entity);

        var anchorUid = entity.Comp.LowerZLevel;
        Del(entity.Owner);

        // Whatever is left crossing this airspace now owns the air this one was using.
        RebuildSliceDepths(anchorUid);
    }

    /// <summary>
    ///     Puts anything still standing on a gap down on the z-level below it before the gap goes away.
    /// </summary>
    /// <remarks>
    ///     Deleting a map deletes everything parented to it, so without this anything that ended up on the
    ///         gap itself rather than on the grid it carries - something that slid off the edge of a
    ///         platform, debris thrown clear of it, a second grid that undocked mid-crossing - is destroyed
    ///         outright the moment the crossing finishes. Set down on the anchor instead, which is the floor
    ///         it would have fallen to anyway.
    /// </remarks>
    private void EvacuateGap(Entity<KsZLevelGapComponent> entity)
    {
        if (!_mapQuery.TryGetComponent(entity.Comp.LowerZLevel, out var anchorMapComponent))
            return;

        _strandedEntities.Clear();

        var childEnumerator = Transform(entity.Owner).ChildEnumerator;
        while (childEnumerator.MoveNext(out var childUid))
        {
            if (childUid != entity.Comp.PrimaryGrid)
                _strandedEntities.Add(childUid);
        }

        foreach (var strandedUid in _strandedEntities)
        {
            if (TerminatingOrDeleted(strandedUid))
                continue;

            _transformSystem.SetMapCoordinates(
                strandedUid,
                new MapCoordinates(_transformSystem.GetWorldPosition(strandedUid), anchorMapComponent.MapId)
            );
        }
    }

    /// <summary>
    ///     The z-level a gap would land on if it stopped where it is.
    /// </summary>
    /// <remarks>
    ///     Halfway is the cut-off, so a crossing abandoned near either end finishes the way it was already
    ///         mostly going rather than reversing.
    /// </remarks>
    public bool TryGetNearestZLevel(
        Entity<KsZLevelGapComponent> entity,
        [NotNullWhen(true)] out Entity<KsZLevelComponent>? zLevelEntity)
    {
        var nearestUid = entity.Comp.Progress >= 0.5f ? entity.Comp.UpperZLevel : entity.Comp.LowerZLevel;

        if (!TryComp<KsZLevelComponent>(nearestUid, out var zLevelComponent))
        {
            // The stack was relinked out from under the crossing. Whichever end still exists will do.
            var fallbackUid = entity.Comp.Progress >= 0.5f ? entity.Comp.LowerZLevel : entity.Comp.UpperZLevel;

            if (!TryComp(fallbackUid, out zLevelComponent))
            {
                zLevelEntity = null;
                return false;
            }

            nearestUid = fallbackUid;
        }

        zLevelEntity = (nearestUid, zLevelComponent);
        return true;
    }

    /// <summary>
    ///     Moves a whole grid onto a map, keeping its world position and its momentum.
    /// </summary>
    /// <remarks>
    ///     Reparenting wipes joints and can reset momentum, so anything the grid was carrying has to be put
    ///         back by hand. A platform is not meant to be moving under its own power, but it can be shoved,
    ///         tugged by a docked shuttle or thrown about by an explosion, and one that quietly came to a
    ///         dead stop every time it changed map would be its own bug report.
    /// </remarks>
    public bool TryMoveGridToMap(EntityUid gridUid, EntityUid mapUid)
    {
        if (!_mapQuery.TryGetComponent(mapUid, out var mapComponent))
            return false;

        var transformComponent = Transform(gridUid);
        if (transformComponent.MapUid == mapUid)
            return true;

        var worldPosition = _transformSystem.GetWorldPosition(transformComponent);

        var hadPhysics = _physicsQuery.TryGetComponent(gridUid, out var physicsComponent);
        var linearVelocity = hadPhysics ? physicsComponent!.LinearVelocity : Vector2.Zero;
        var angularVelocity = hadPhysics ? physicsComponent!.AngularVelocity : 0f;

        // SetMapCoordinates special-cases grids: it parents them straight to the map rather than snapping
        //      them under whichever grid happens to occupy that spot, which is exactly what moving a whole
        //      grid wants. Everything anchored to or standing on it rides along for free, because none of
        //      their own parents change.
        _transformSystem.SetMapCoordinates(gridUid, new MapCoordinates(worldPosition, mapComponent.MapId));

        if (hadPhysics)
        {
            _physicsSystem.SetLinearVelocity(gridUid, linearVelocity, body: physicsComponent);
            _physicsSystem.SetAngularVelocity(gridUid, angularVelocity, body: physicsComponent);
        }

        return !TerminatingOrDeleted(gridUid);
    }

    /// <summary>
    ///     Gives a fresh gap map the air, gravity, ambient light and parallax of the z-levels it bridges.
    /// </summary>
    /// <remarks>
    ///     None of this is optional. Without an atmosphere the grid's edge tiles vent to space on every
    ///         crossing and <see cref="Content.Server.Chat.Systems.FarSoundSystem"/> stops treating the map as
    ///         real; without gravity everyone aboard floats for the length of the ride. The upper z-level is
    ///         preferred because a gap is a hole in its floor, and so shares its air before anything else's.
    /// </remarks>
    private void CopyEnvironment(EntityUid gapUid, EntityUid preferredUid, EntityUid fallbackUid)
    {
        if (TryComp<MapAtmosphereComponent>(preferredUid, out var atmosphereComponent) ||
            TryComp(fallbackUid, out atmosphereComponent))
            _atmosphereSystem.SetMapAtmosphere(gapUid, atmosphereComponent.Space, atmosphereComponent.Mixture);

        if (TryComp<GravityComponent>(preferredUid, out var gravityComponent) ||
            TryComp(fallbackUid, out gravityComponent))
        {
            var gapGravityComponent = EnsureComp<GravityComponent>(gapUid);
            gapGravityComponent.Enabled = gravityComponent.Enabled;
            gapGravityComponent.Inherent = gravityComponent.Inherent;
            Dirty(gapUid, gapGravityComponent);
        }

        if (TryComp<MapLightComponent>(preferredUid, out var mapLightComponent) ||
            TryComp(fallbackUid, out mapLightComponent))
            _mapSystem.SetAmbientLight(_mapQuery.GetComponent(gapUid).MapId, mapLightComponent.AmbientLightColor);

        // Copied for the opposite of the obvious reason. A map with no ParallaxComponent is drawn with the
        //      *default* parallax, not with none - and a full-screen backdrop on a gap would paint over the
        //      z-levels its pass is composited on top of. Mapped z-levels carry a ParallaxComponent naming
        //      an invalid parallax precisely to switch that off, so copying it is what keeps a gap
        //      transparent.
        if (TryComp<ParallaxComponent>(preferredUid, out var parallaxComponent) ||
            TryComp(fallbackUid, out parallaxComponent))
        {
            var gapParallaxComponent = EnsureComp<ParallaxComponent>(gapUid);
            gapParallaxComponent.Parallax = parallaxComponent.Parallax;
            Dirty(gapUid, gapParallaxComponent);
        }
    }

    /// <summary>
    ///     Deletes gaps whose grid has gone away.
    /// </summary>
    /// <remarks>
    ///     A gap exists only to carry one grid. Blow that grid up mid-crossing - which players will - and
    ///         without this the map stays for the rest of the round, holding an override on a deleted entity.
    /// </remarks>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _orphanedGaps.Clear();

        var enumerator = EntityQueryEnumerator<KsZLevelGapComponent>();
        while (enumerator.MoveNext(out var uid, out var gapComponent))
        {
            if (gapComponent.PrimaryGrid is { } gridUid &&
                !TerminatingOrDeleted(gridUid) &&
                Transform(gridUid).MapUid == uid)
                continue;

            _orphanedGaps.Add((uid, gapComponent));
        }

        foreach (var orphanedEntity in _orphanedGaps)
            DestroyGap(orphanedEntity);
    }
}
