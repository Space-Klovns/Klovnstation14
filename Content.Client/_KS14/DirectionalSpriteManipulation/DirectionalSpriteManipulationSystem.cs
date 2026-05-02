using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Map;
using Robust.Shared.Reflection;

namespace Content.Client._KS14.DirectionalSpriteManipulation;

public sealed class DirectionalSpriteManipulationSystem : EntitySystem
{
    [Dependency] private readonly IReflectionManager _reflectionManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;

        SubscribeLocalEvent<DirectionalSpriteManipulationComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<DirectionalSpriteManipulationComponent> entity, ref ComponentInit args)
    {
        foreach (var (key, value) in entity.Comp.LayerDataMappings!)
            entity.Comp.LayerData[ParseKey(key)] = value;

        entity.Comp.LayerDataMappings = null;
    }

    // This makes more sense to be FrameUpdate but it doesn't matter and it wastes more performance.
    public override void Update(float dt)
    {
        var eqe = AllEntityQuery<DirectionalSpriteManipulationComponent, SpriteComponent, TransformComponent>();
        var eyeRotation = _eyeManager.CurrentEye.Rotation;

        while (eqe.MoveNext(out var uid, out var directionalOffsetComponent, out var spriteComponent, out var transformComponent))
        {
            if (transformComponent.MapID == MapId.Nullspace)
                continue;

            var effectiveRotation = _transformSystem.GetWorldRotation(transformComponent, EntityManager.TransformQuery) + eyeRotation;

            foreach (var (layerKey, layerData) in directionalOffsetComponent.LayerData)
            {
                // this will throw if the layer doesn't exist
                if (spriteComponent[layerKey] is not SpriteComponent.Layer layer)
                    continue;

                var effectiveDirection = DirExt.ToRsiDirection(effectiveRotation, directionalOffsetComponent.OverrideRsiDirections ?? layer.ActualState!.RsiDirections);
                Vector2 offset;

                if (!layerData.TryGetValue(effectiveDirection, out var directionalData)) // default to no offset
                    offset = Vector2.Zero;
                else
                {
                    offset = directionalData.Offset;
                    if (directionalData.Rotation is { } rotation)
                        _spriteSystem.LayerSetRotation(layer, rotation);
                }

                _spriteSystem.LayerSetOffset(layer, offset);
            }
        }
    }

    private object ParseKey(string keyString)
    {
        if (_reflectionManager.TryParseEnumReference(keyString, out var @enum))
            return @enum;

        return keyString;
    }
}
