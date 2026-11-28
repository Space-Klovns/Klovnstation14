using Content.Shared._KS14.WallHealer;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.WallHealer;

/// <summary>
///     Shows how full each of a wall healer's pools is, on the sprite layer the pool names.
/// </summary>
public sealed partial class KsWallHealerVisualizerSystem : EntitySystem
{
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private KsWallHealerSystem _wallHealerSystem = default!;

    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsWallHealerComponent> healer, ref ComponentStartup args)
    {
        UpdatePoolLayers(healer);
    }

    [SubscribeLocalEvent]
    private void OnAfterHandleState(Entity<KsWallHealerComponent> healer, ref AfterAutoHandleStateEvent args)
    {
        UpdatePoolLayers(healer);
    }

    [SubscribeLocalEvent]
    private void OnPoolsChanged(Entity<KsWallHealerComponent> healer, ref KsWallHealerPoolsChangedEvent args)
    {
        UpdatePoolLayers(healer);
    }

    private void UpdatePoolLayers(Entity<KsWallHealerComponent> healer)
    {
        if (!_spriteQuery.TryComp(healer, out var spriteComponent))
            return;

        var fillSteps = Math.Max(healer.Comp.FillSteps, 1);
        for (var i = 0; i < healer.Comp.Pools.Count; i++)
        {
            if (healer.Comp.Pools[i].Layer is not { } layerKey
                || !_spriteSystem.LayerMapTryGet((healer.Owner, spriteComponent), layerKey, out var layerIndex, false))
                continue;

            // State 0 is full and the last is empty.
            var step = fillSteps - (int)MathF.Round(fillSteps * _wallHealerSystem.GetPoolFill(healer, i));
            _spriteSystem.LayerSetRsiState((healer.Owner, spriteComponent), layerIndex, $"{healer.Comp.FillStatePrefix}{step}");
        }
    }
}
