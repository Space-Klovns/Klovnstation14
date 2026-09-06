using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Projectiles;
using Content.Shared.Stunnable;
using Content.Shared.SubFloor;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Weapons.Ranged;

public sealed partial class GunBackblastSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private TileSystem _tileSystem = default!;

    [Dependency] private EntityQuery<SubFloorHideComponent> _subFloorHideQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunBackblastComponent, GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(Entity<GunBackblastComponent> entity, ref GunShotEvent args)
    {
        var gunTransform = Transform(entity);

        var fromWorldPosition = _transformSystem.ToWorldPosition(args.FromCoordinates);
        var uidsInRange = _entityLookupSystem.GetEntitiesInRange(gunTransform.MapID, fromWorldPosition, entity.Comp.Radius, LookupFlags.Uncontained | LookupFlags.Dynamic);
        if (uidsInRange.Count == 0)
            return;

        var sectorDirection = ((_transformSystem.ToWorldPosition(args.ToCoordinates) - fromWorldPosition).ToWorldAngle() + entity.Comp.DirectionOffset).ToWorldVec();
        var halfField = entity.Comp.EffectField / 2d;

        var gridUid = _transformSystem.GetGrid(args.FromCoordinates);

        if (entity.Comp.TilebreakChance > 0f &&
            gridUid is { } validGridUid &&
            TryComp<MapGridComponent>(validGridUid, out var mapGridComponent))
        {
            TryBreakTiles((validGridUid, mapGridComponent), entity, fromWorldPosition, sectorDirection, halfField);
        }

        foreach (var otherUid in uidsInRange)
        {
            if (otherUid == args.User ||
                otherUid == entity.Owner)
                continue;

            // don't damage this entity if it's covered by a subfloor
            if (_subFloorHideQuery.TryGetComponent(otherUid, out var subFloorHideComponent) &&
                subFloorHideComponent.IsUnderCover)
                continue;

            // more expensive checks
            if (HasComp<ProjectileComponent>(otherUid) ||
                args.Ammo.Any(x => x.Uid == otherUid) ||
                !IsInCone(fromWorldPosition, otherUid, sectorDirection, halfField, out var toWorldPosition, out var toDirectionUnitVector) ||
                !_interactionSystem.InRangeUnobstructed(new MapCoordinates(fromWorldPosition, gunTransform.MapID), new MapCoordinates(toWorldPosition, gunTransform.MapID), range: -1))
                continue;

            if (TryComp<PhysicsComponent>(otherUid, out var physicsComponent))
                _physicsSystem.ApplyLinearImpulse(otherUid, toDirectionUnitVector * entity.Comp.PushForce, body: physicsComponent);

            _damageableSystem.TryChangeDamage(otherUid, entity.Comp.Damage, origin: args.User);
            _stunSystem.TryKnockdown(otherUid, entity.Comp.KnockdownTime, refresh: false);
        }
    }

    private bool IsInCone(Vector2 fromWorldPosition, EntityUid otherUid, Vector2 sectorUnitDirection, Angle halfField, out Vector2 toWorldPosition, out Vector2 toDirectionUnitVector)
    {
        toWorldPosition = _transformSystem.GetWorldPosition(otherUid);
        toDirectionUnitVector = toWorldPosition - fromWorldPosition;
        Vector2Helpers.Normalize(ref toDirectionUnitVector);

        return Vector2.Dot(sectorUnitDirection, toDirectionUnitVector) >=
               MathF.Cos((float)halfField.Theta);
    }

    /// <summary>
    /// Rolls <see cref="GunBackblastComponent.TilebreakChance"/> for every tile touching the backblast's
    /// circular sector - not just tiles fully engulfed by it - and pries the ones that pass.
    /// </summary>
    private void TryBreakTiles(Entity<MapGridComponent> grid, Entity<GunBackblastComponent> entity, Vector2 fromWorldPosition, Vector2 sectorUnitDirection, Angle halfField)
    {
        var radius = entity.Comp.Radius;
        var worldSearchBounds = new Box2(
            fromWorldPosition.X - radius,
            fromWorldPosition.Y - radius,
            fromWorldPosition.X + radius,
            fromWorldPosition.Y + radius
        );

        // Cheap broadphase pass - only the tiles inside the sector's bounding square are worth the precise
        // per-tile check below.
        var candidateTiles = _mapSystem.GetTilesIntersecting(grid.Owner, grid.Comp, worldSearchBounds, ignoreEmpty: true);

        var worldMatrix = _transformSystem.GetWorldMatrix(grid.Owner);
        var cosHalfField = MathF.Cos((float)halfField.Theta);

        // One Random for the whole shot - Prob() mutates its state on every call, so consuming it
        // sequentially across tiles already gives each tile an independent-looking roll without paying for a
        // fresh seed+instantiation per tile. Determinism between client prediction and the server just needs
        // both sides to seed it identically and walk the same (deterministic) tile order, not a per-tile seed.
        // The truncated origin is thrown into the seed too - cheap extra entropy so two guns firing on the
        // same tick (or the same gun firing twice in one tick) don't roll identical tile-break sequences.
        var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
            (int)_gameTiming.CurTick.Value,
            KsSharedRandomExtensions.GetNetId(entity.Owner, EntityManager),
            (int)fromWorldPosition.X,
            (int)fromWorldPosition.Y
        );

        foreach (var tileRef in candidateTiles)
        {
            if (!IsTileTouchingSector(tileRef.GridIndices, grid.Comp, worldMatrix, fromWorldPosition, sectorUnitDirection, cosHalfField, radius))
                continue;

            if (predictedRandom.Prob(entity.Comp.TilebreakChance))
                _tileSystem.PryTile(tileRef);
        }
    }

    /// <summary>
    /// Approximates tile-vs-sector overlap by sampling the tile's center and four corners in world space
    /// (honouring grid rotation) - if any sampled point is both within <paramref name="radius"/> and inside
    /// the <paramref name="halfField"/>-wide cone around <paramref name="sectorUnitDirection"/>, the tile
    /// counts as touching the AOE, even if most of the tile falls outside it.
    /// </summary>
    private static bool IsTileTouchingSector(
        Vector2i tileIndices,
        MapGridComponent grid,
        Matrix3x2 worldMatrix,
        Vector2 fromWorldPosition,
        Vector2 sectorUnitDirection,
        float cosHalfField,
        float radius)
    {
        var tileSize = grid.TileSize;
        var localOrigin = new Vector2(tileIndices.X * tileSize, tileIndices.Y * tileSize);
        var localHalf = grid.TileSizeHalfVector;

        //  [ERRO] res.typecheck: Found reference to(MethodDefinition handle) <<> y__InlineArray5`1 < [System.Numerics.Vectors]System.Numerics.Vector2 >, [System.Numerics.Vectors] System.Numerics.Vector2 > in method Content.Shared._KS14.Weapons.Ranged.GunBackblastSystem.IsTileTouchingSector at IL 0x00AF
        //  [ERRO] res.typecheck: Found reference to(MethodDefinition handle) <<> y__InlineArray5`1 < [System.Numerics.Vectors]System.Numerics.Vector2 >, [System.Numerics.Vectors] System.Numerics.Vector2 > in method Content.Shared._KS14.Weapons.Ranged.GunBackblastSystem.IsTileTouchingSector at IL 0x00AF
        //  [ERRO] res.typecheck: Found reference to(MethodDefinition handle) <<> y__InlineArray5`1 < [System.Numerics.Vectors]System.Numerics.Vector2 >, [System.Numerics.Vectors] System.Numerics.Vector2 > in method Content.Shared._KS14.Weapons.Ranged.GunBackblastSystem.IsTileTouchingSector at IL 0x00AF
        //  [ERRO] res.typecheck: Found reference to(MethodDefinition handle) <<> y__InlineArray5`1 < [System.Numerics.Vectors]System.Numerics.Vector2 >, [System.Numerics.Vectors] System.Numerics.Vector2 > in method Content.Shared._KS14.Weapons.Ranged.GunBackblastSystem.IsTileTouchingSector at IL 0x00AF
        //  [ERRO] res.typecheck: Found reference to(MethodDefinition handle) <<> y__InlineArray5`1 < [System.Numerics.Vectors]System.Numerics.Vector2 >, [System.Numerics.Vectors] System.Numerics.Vector2 > in method Content.Shared._KS14.Weapons.Ranged.GunBackblastSystem.IsTileTouchingSector at IL 0x00AF
        //  [ERRO] res.typecheck: Found reference to(MethodDefinition handle) <<> y__InlineArray5`1 < [System.Numerics.Vectors]System.Numerics.Vector2 >, [System.Numerics.Vectors] System.Numerics.Vector2 > in method Content.Shared._KS14.Weapons.Ranged.GunBackblastSystem.IsTileTouchingSector at IL 0x00AF
        //  [ERRO] res.typecheck: Found reference to(MethodDefinition handle) <<> y__InlineArray5`1 < [System.Numerics.Vectors]System.Numerics.Vector2 >, [System.Numerics.Vectors] System.Numerics.Vector2 > in method Content.Shared._KS14.Weapons.Ranged.GunBackblastSystem.IsTileTouchingSector at IL 0x00AF
        //  [ERRO] res.typecheck: Found reference to(MethodDefinition handle) <<> y__InlineArray5`1 < [System.Numerics.Vectors]System.Numerics.Vector2 >, [System.Numerics.Vectors] System.Numerics.Vector2 > in method Content.Shared._KS14.Weapons.Ranged.GunBackblastSystem.IsTileTouchingSector at IL 0x00AF
        //  [ERRO] res.typecheck: Found reference to(MethodDefinition handle) <<> y__InlineArray5`1 < [System.Numerics.Vectors]System.Numerics.Vector2 >, [System.Numerics.Vectors] System.Numerics.Vector2 > in method Content.Shared._KS14.Weapons.Ranged.GunBackblastSystem.IsTileTouchingSector at IL 0x00AF
        Span<Vector2> localSamples = new Vector2[] {
            localOrigin + localHalf, // Center.
            localOrigin, // Bottom-left.
            localOrigin + new Vector2(tileSize, 0f), // Bottom-right.
            localOrigin + new Vector2(0f, tileSize), // Top-left.
            localOrigin + new Vector2(tileSize, tileSize), // Top-right.
        };

        foreach (var localSample in localSamples)
        {
            var worldSample = Vector2.Transform(localSample, worldMatrix);
            var toSample = worldSample - fromWorldPosition;
            var distance = toSample.Length();

            if (distance > radius)
                continue;

            // The shooter's own tile - always "touching" regardless of angle, sector originates inside it.
            if (distance <= 0.0001f)
                return true;

            if (Vector2.Dot(sectorUnitDirection, toSample / distance) >= cosHalfField)
                return true;
        }

        return false;
    }
}
