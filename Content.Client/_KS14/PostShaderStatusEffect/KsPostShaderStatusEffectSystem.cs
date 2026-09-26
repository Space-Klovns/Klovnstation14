using Content.Client._KS14.Graphics;
using Content.Client._KS14.StatusEffect;
using Content.Shared.StatusEffectNew.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._KS14.PostShaderStatusEffect;

/// <summary>
///     Keeps the post-shader of every <see cref="KsPostShaderStatusEffectComponent"/> effect on the sprite of the
///         entity the effect is on, for exactly as long as the effect is active there.
/// </summary>
public sealed partial class KsPostShaderStatusEffectSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsPostShaderStatusEffectComponent> entity, ref ComponentShutdown args)
    {
        RemoveFromShaded(entity);

        entity.Comp.ShaderInstance?.Dispose();
        entity.Comp.ShaderInstance = null;
    }

    // Reconciled every frame rather than on the applied and removed events: those don't cover an effect that starts
    //      late, a sprite that arrives after the effect does, or an effect entity that comes into view already applied.
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var curTime = _gameTiming.CurTime;
        var effectEnumerator = EntityQueryEnumerator<KsPostShaderStatusEffectComponent, StatusEffectComponent>();
        while (effectEnumerator.MoveNext(out var effectUid, out var postShaderEffectComponent, out var statusEffectComponent))
        {
            var effect = new Entity<KsPostShaderStatusEffectComponent>(effectUid, postShaderEffectComponent);

            // Null while not applied to anything, or delayed and not started yet.
            var targetUid = statusEffectComponent.StartEffectTime <= curTime ? statusEffectComponent.AppliedTo : null;

            if (postShaderEffectComponent.ShadedUid is { } shadedUid && shadedUid != targetUid)
                RemoveFromShaded(effect);

            if (targetUid is not { } appliedToUid ||
                !_spriteQuery.TryGetComponent(appliedToUid, out var spriteComponent))
                continue;

            var shaderInstance = EnsureShaderInstance(effect);
            if (postShaderEffectComponent.TimeLeftParameter is { } timeLeftParameter)
                shaderInstance.SetParameter(timeLeftParameter, KsStatusEffectTimeLeft.GetRatio(statusEffectComponent, curTime));

            var postShaderId = GetPostShaderId(effectUid);
            if (!_spriteSystem.HasPostShader(spriteComponent, postShaderId))
            {
                _spriteSystem.SetPostShader(spriteComponent,
                    new SpriteComponent.PostShaderArgs(postShaderId, shaderInstance)
                    {
                        Before = KsPostShaderIds.BeforeOutlines,
                    });
            }

            postShaderEffectComponent.ShadedUid = appliedToUid;
        }
    }

    /// <summary>
    ///     Gets this effect's shader instance, creating it and setting its parameters the first time.
    /// </summary>
    private ShaderInstance EnsureShaderInstance(Entity<KsPostShaderStatusEffectComponent> effect)
    {
        if (effect.Comp.ShaderInstance is { } existingShaderInstance)
            return existingShaderInstance;

        var shaderInstance = ProtoMan.Index(effect.Comp.Shader).InstanceUnique();
        foreach (var (name, parameter) in effect.Comp.Parameters)
            parameter.Apply(shaderInstance, name);

        // Kept small: a float only holds whole numbers exactly up to 2^24, and shaders tend to multiply this.
        if (effect.Comp.SeedParameter is { } seedParameter)
            shaderInstance.SetParameter(seedParameter, (float)(effect.Owner.Id % 1000));

        effect.Comp.ShaderInstance = shaderInstance;
        return shaderInstance;
    }

    /// <summary>
    ///     Takes this effect's post-shader back off the sprite it is on, if it is on one.
    /// </summary>
    private void RemoveFromShaded(Entity<KsPostShaderStatusEffectComponent> effect)
    {
        if (effect.Comp.ShadedUid is not { } shadedUid)
            return;

        effect.Comp.ShadedUid = null;

        if (_spriteQuery.TryGetComponent(shadedUid, out var spriteComponent))
            _spriteSystem.RemovePostShader(spriteComponent, GetPostShaderId(effect.Owner));
    }

    /// <summary>
    ///     One post-shader id per effect entity, so several of these effects can sit on one sprite at once.
    /// </summary>
    private static string GetPostShaderId(EntityUid effectUid)
    {
        return $"{KsPostShaderIds.StatusEffectPrefix}{effectUid.Id}";
    }
}
