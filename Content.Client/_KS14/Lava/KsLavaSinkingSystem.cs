using Content.Client._KS14.Graphics;
using Content.Shared._KS14.Lava;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Lava;

/// <summary>
///     Not my proudest code yet
/// </summary>
public sealed partial class KsLavaSinkingSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    private static readonly ProtoId<ShaderPrototype> ShaderId = "HorizontalCut";

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsLavaSinkingComponent> entity, ref ComponentStartup args)
    {
        SetShaderEnabled(entity, true);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsLavaSinkingComponent> entity, ref ComponentShutdown args)
    {
        SetShaderEnabled(entity, false);
    }

    private void SetShaderEnabled(Entity<KsLavaSinkingComponent> entity, bool enabled)
    {
        if (!TryComp<SpriteComponent>(entity.Owner, out var spriteComponent))
            return;

        if (!enabled)
        {
            _spriteSystem.RemovePostShader((entity.Owner, spriteComponent), KsPostShaderIds.LavaSinking);
            return;
        }

        var shaderInstance = ProtoMan.Index(ShaderId).InstanceUnique();

        _spriteSystem.SetPostShader((entity.Owner, spriteComponent),
            new SpriteComponent.PostShaderArgs(KsPostShaderIds.LavaSinking, shaderInstance)
            {
                RaiseShaderEvent = true,
            });
    }

    [SubscribeLocalEvent]
    private void OnShaderRender(Entity<KsLavaSinkingComponent> entity, ref BeforePostShaderRenderEvent args)
    {
        // The event now fires once per post-shader entry on the sprite, so only answer for ours.
        if (args.Id != KsPostShaderIds.LavaSinking)
            return;

        var time = (float)((entity.Comp.SinkTime - _gameTiming.CurTime) / (entity.Comp.SinkTime - entity.Comp.StartTime));
        time = MathF.Max(time, 0f);

        args.Shader.SetParameter("c", 1 - time);
        args.Shader.SetParameter("alphaModifier", MathF.Max(time - 0.25f, 0f));
    }
}
