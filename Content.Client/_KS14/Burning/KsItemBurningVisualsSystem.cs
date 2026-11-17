using Content.Shared._KS14.Burning;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Burning;

/// <summary>
///     Draws flames over a burning item, tgstation's burning overlay.
/// </summary>
public sealed partial class KsItemBurningVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    private static readonly SpriteSpecifier.Rsi FireSprite = new(new ResPath("/Textures/Effects/fire.rsi"), "fire");

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsItemBurningComponent> entity, ref ComponentStartup args)
    {
        if (!_spriteQuery.TryComp(entity.Owner, out var spriteComponent) ||
            _spriteSystem.LayerMapTryGet((entity.Owner, spriteComponent), KsItemBurningVisualLayers.Fire, out _, logMissing: false))
            return;

        var layer = _spriteSystem.AddLayer((entity.Owner, spriteComponent), FireSprite);
        _spriteSystem.LayerMapSet((entity.Owner, spriteComponent), KsItemBurningVisualLayers.Fire, layer);
        spriteComponent.LayerSetShader(layer, "unshaded");
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsItemBurningComponent> entity, ref ComponentShutdown args)
    {
        if (!_spriteQuery.TryComp(entity.Owner, out var spriteComponent) ||
            !_spriteSystem.LayerMapTryGet((entity.Owner, spriteComponent), KsItemBurningVisualLayers.Fire, out var layer, logMissing: false))
            return;

        _spriteSystem.RemoveLayer((entity.Owner, spriteComponent), layer);
    }

    private enum KsItemBurningVisualLayers : byte
    {
        Fire,
    }
}
