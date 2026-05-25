using Content.Shared._KS14.Lava;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Lava;

/// <summary>
///     Not my proudest code yet
/// </summary>
public sealed class KsLavaSinkingSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private static readonly ProtoId<ShaderPrototype> ShaderId = "HorizontalCut";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsLavaSinkingComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KsLavaSinkingComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<KsLavaSinkingComponent, BeforePostShaderRenderEvent>(OnShaderRender);
    }

    private void OnStartup(Entity<KsLavaSinkingComponent> entity, ref ComponentStartup args)
    {
        SetShaderEnabled(entity, true);
    }

    private void OnShutdown(Entity<KsLavaSinkingComponent> entity, ref ComponentShutdown args)
    {
        SetShaderEnabled(entity, false);
    }

    private void SetShaderEnabled(Entity<KsLavaSinkingComponent> entity, bool enabled)
    {
        if (!TryComp<SpriteComponent>(entity.Owner, out var spriteComponent))
            return;

        entity.Comp.Shader = enabled ? _prototypeManager.Index(ShaderId).InstanceUnique() : null;
        spriteComponent.PostShader = (ShaderInstance?)entity.Comp.Shader;
        spriteComponent.RaiseShaderEvent = enabled;
    }

    private void OnShaderRender(Entity<KsLavaSinkingComponent> entity, ref BeforePostShaderRenderEvent args)
    {
        var time = (float)((entity.Comp.SinkTime - _gameTiming.CurTime) / (entity.Comp.SinkTime - entity.Comp.StartTime));
        time = MathF.Max(time, 0f);

        var shaderInstance = (ShaderInstance)entity.Comp.Shader!;
        shaderInstance.SetParameter("c", 1 - time);
        shaderInstance.SetParameter("alphaModifier", MathF.Max(time - 0.25f, 0f));
    }
}
