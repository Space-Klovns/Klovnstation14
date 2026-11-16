using System.Linq;
using System.Numerics;
using Content.Server.Decals;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Shared._KS14.Lawnmower;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Triggers;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Lawnmower;

public sealed partial class KsLawnmowerBladesSystem : SharedKsLawnmowerBladesSystem
{
    [Dependency] private DecalSystem _decalSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitions = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;
    [Dependency] private SharedChatSystem _chatSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    private static readonly ProtoId<ContentTileDefinition> MowedAstroGrass = "FloorMowedAstroGrass";
    private static readonly ProtoId<DecalPrototype> WheelTracks = "KsLawnmowerBloodTracks";
    private static readonly ProtoId<EmotePrototype> Scream = "Scream";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly ResPath GrassDecalSprite = new("/Textures/Decals/Flora/flora_grass.rsi");

    protected override void MowTile(Entity<KsLawnmowerBladesComponent> mower, TransformComponent transformComponent)
    {
        if (transformComponent.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var gridComponent) ||
            !_mapSystem.TryGetTileRef(gridUid, gridComponent, transformComponent.Coordinates, out var tile) ||
            tile.Tile.IsEmpty)
        {
            mower.Comp.LastTile = null;
            return;
        }

        var tileKey = (gridUid, tile.GridIndices);
        if (mower.Comp.LastTile == tileKey)
            return;

        mower.Comp.LastTile = tileKey;
        if (mower.Comp.MowableTiles.Contains(_tileDefinitions[tile.Tile.TypeId].ID))
            _mapSystem.SetTile(gridUid, gridComponent, tile.GridIndices, new Tile(ProtoMan.Index(MowedAstroGrass).TileId));

        var tileOrigin = new Vector2(tile.GridIndices.X, tile.GridIndices.Y) * gridComponent.TileSize;
        var tileCenter = tileOrigin + new Vector2(gridComponent.TileSize * 0.5f);
        foreach (var (index, decal) in _decalSystem.GetDecalsInRange(gridUid, tileCenter, distance: gridComponent.TileSize * 0.71f))
        {
            var decalCenter = decal.Coordinates + new Vector2(0.5f);
            if (decalCenter.X < tileOrigin.X || decalCenter.X >= tileOrigin.X + gridComponent.TileSize ||
                decalCenter.Y < tileOrigin.Y || decalCenter.Y >= tileOrigin.Y + gridComponent.TileSize ||
                !ProtoMan.TryIndex<DecalPrototype>(decal.Id, out var prototype) ||
                prototype.Sprite is not SpriteSpecifier.Rsi sprite || sprite.RsiPath != GrassDecalSprite)
            {
                continue;
            }

            _decalSystem.RemoveDecal(gridUid, index);
        }

        if (mower.Comp.BloodyTiles <= 0)
            return;

        var rotation = _transformSystem.GetWorldRotation(transformComponent) - _transformSystem.GetWorldRotation(gridUid);
        if (_decalSystem.TryAddDecal(WheelTracks.Id, new EntityCoordinates(gridUid, tileOrigin), out _,
            color: Color.FromHex("#8f1818").WithAlpha(mower.Comp.BloodyTiles / 4f), rotation: rotation, cleanable: true))
        {
            mower.Comp.BloodyTiles--;
        }
    }

    protected override void CrushVictim(Entity<KsLawnmowerBladesComponent> mower, EntityUid victimUid, EntityUid riderUid)
    {
        if (_mobStateSystem.IsAlive(victimUid))
            _chatSystem.TryEmoteWithChat(victimUid, ProtoMan.Index(Scream), ignoreActionBlocker: true);

        ApplyCrushDamage(victimUid, riderUid, mower.Comp.CrushDamage);
    }

    private void ApplyCrushDamage(EntityUid victimUid, EntityUid riderUid, FixedPoint2 amount)
    {
        if (!TryComp<DamageableComponent>(victimUid, out var damageableComponent))
            return;

        var currentDamage = _damageableSystem.GetPositiveDamage((victimUid, damageableComponent));
        var totalDamage = currentDamage.GetTotal();
        if (_mobThresholdSystem.TryGetDeadThreshold(victimUid, out var lethalDamage))
            amount = FixedPoint2.Min(amount, lethalDamage.Value + 10 - totalDamage);

        // Keep the corpse intact even for species whose death and gib thresholds are close together.
        if (TryComp<DestructibleComponent>(victimUid, out var destructibleComponent))
        {
            var bluntDamage = currentDamage.DamageDict.GetValueOrDefault(Blunt);
            foreach (var threshold in destructibleComponent.Thresholds)
            {
                if (!threshold.Behaviors.Any(behavior => behavior is GibBehavior))
                    continue;

                var remaining = threshold.Trigger switch
                {
                    DamageTrigger total => total.Damage - totalDamage,
                    DamageTypeTrigger blunt when blunt.DamageType == Blunt => blunt.Damage - bluntDamage,
                    _ => FixedPoint2.MaxValue,
                };

                amount = FixedPoint2.Min(amount, remaining - FixedPoint2.Epsilon);
            }
        }

        if (amount > FixedPoint2.Zero)
        {
            var damage = new DamageSpecifier(ProtoMan.Index(Blunt), amount);
            _damageableSystem.TryChangeDamage((victimUid, damageableComponent), damage,
                ignoreResistances: true, origin: riderUid, ignoreGlobalModifiers: true);
        }
    }
}
