using System.Linq;
using Content.Client.DisplacementMap;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.DisplacementMap;

public sealed partial class KsAlwaysDisplacedSystem : EntitySystem
{
    [Dependency] private DisplacementMapSystem _displacementMapSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsAlwaysDisplacedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KsAlwaysDisplacedComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<KsAlwaysDisplacedComponent> entity, ref ComponentStartup args)
    {
        if (!_spriteQuery.TryComp(entity.Owner, out var spriteComponent))
            return;

        var spriteEntity = new Entity<SpriteComponent>(entity.Owner, spriteComponent);
        var layerCount = spriteComponent.AllLayers.Count();

        // Insert back-to-front: each insertion pushes the layer it targets (and everything after it) up by one
        // index, so processing higher indices first keeps the indices of layers we haven't visited yet stable.
        for (var index = layerCount - 1; index >= 0; index--)
        {
            var layerKey = index.ToString();

            // The displacement's shader-copy step resolves its target layer through the sprite's layer map at
            // render time, so the key needs a map entry pointing at this layer before we can use it as a key.
            // Layer map values auto-shift when a layer is inserted before them, so this stays correct afterwards.
            _spriteSystem.LayerMapSet(spriteEntity.AsNullable(), layerKey, index);

            if (!_displacementMapSystem.TryAddDisplacement(entity.Comp.Displacement, spriteEntity, index, layerKey, out _))
                continue;

            entity.Comp.DisplacedLayerKeys.Add(layerKey);
        }
    }

    private void OnShutdown(Entity<KsAlwaysDisplacedComponent> entity, ref ComponentShutdown args)
    {
        if (!_spriteQuery.TryComp(entity.Owner, out var spriteComponent))
            return;

        var spriteEntity = new Entity<SpriteComponent>(entity.Owner, spriteComponent);
        foreach (var layerKey in entity.Comp.DisplacedLayerKeys)
            _displacementMapSystem.EnsureDisplacementIsNotOnSprite(spriteEntity, layerKey);

        entity.Comp.DisplacedLayerKeys.Clear();
    }
}
