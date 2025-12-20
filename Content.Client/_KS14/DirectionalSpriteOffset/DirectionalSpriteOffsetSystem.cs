using System.Numerics;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.DirectionalSpriteOffset;

public sealed class DirectionalSpriteOffsetSystem : EntitySystem
{
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
    }

    public override void Update(float dt)
    {
        // this could actually be AllEQE but i think that's way too laggy
        var eqe = EntityQueryEnumerator<DirectionalSpriteOffsetComponent, SpriteComponent>();

        while (eqe.MoveNext(out var uid, out var directionalOffsetComponent, out var spriteComponent))
        {
            // worldRotation isn't calculated until we actually use it,
            // and in that case it becomes cached for future uses in this iteration
            Angle? worldRotation = null;

            foreach (var (layerKey, layerData) in directionalOffsetComponent.LayerOffsetData)
            {
                // this will throw if the layer doesn't exist
                if (spriteComponent[layerKey] is not SpriteComponent.Layer layer)
                    continue;

                worldRotation ??= _transformSystem.GetWorldRotation(Transform(uid), EntityManager.TransformQuery);

                // default to no offset
                if (!layerData.TryGetValue(layer.EffectiveDirection(layer.ActualState!, worldRotation.Value, overrideDirection: null), out var directionalOffset))
                    directionalOffset = Vector2.Zero;

                _spriteSystem.LayerSetOffset(layer, directionalOffset);
            }
        }
    }
}
