using Content.Shared._KS14.Perks.Tank;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Perks.Tank;

/// <summary>
///     Draws the living shield on its holder while it is up.
/// </summary>
public sealed partial class KsLivingShieldVisualsSystem : EntitySystem
{
    private const string LayerKey = "ks-living-shield";

    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsLivingShieldAbilityComponent> entity, ref ComponentStartup args) => UpdateLayer(entity);

    [SubscribeLocalEvent]
    private void OnAfterState(Entity<KsLivingShieldAbilityComponent> entity, ref AfterAutoHandleStateEvent args) => UpdateLayer(entity);

    [SubscribeLocalEvent]
    private void OnRemove(Entity<KsLivingShieldAbilityComponent> entity, ref ComponentRemove args) // the shared system has ComponentShutdown
    {
        if (!TerminatingOrDeleted(entity.Owner) && _spriteQuery.TryComp(entity.Owner, out var spriteComponent))
            _spriteSystem.RemoveLayer((entity.Owner, spriteComponent), LayerKey, logMissing: false);
    }

    private void UpdateLayer(Entity<KsLivingShieldAbilityComponent> entity)
    {
        if (!_spriteQuery.TryComp(entity.Owner, out var spriteComponent))
            return;

        var sprite = new Entity<SpriteComponent?>(entity.Owner, spriteComponent);
        if (!_spriteSystem.LayerMapTryGet(sprite, LayerKey, out var layerIndex, logMissing: false))
        {
            if (!entity.Comp.Raised)
                return;

            layerIndex = _spriteSystem.AddLayer(sprite, entity.Comp.ShieldSprite);
            _spriteSystem.LayerMapSet(sprite, LayerKey, layerIndex);
        }

        _spriteSystem.LayerSetVisible(sprite, layerIndex, entity.Comp.Raised);
    }
}
