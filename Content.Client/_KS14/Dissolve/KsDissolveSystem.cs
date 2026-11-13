using Content.Client._KS14.Graphics;
using Content.Shared._KS14.Dissolve;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Dissolve;

/// <summary>
///     Dissolves the whole sprite of an entity with a <see cref="KsDissolveComponent"/>, as one image, with a
///         post-shader, and hides it at the end until the server's deletion arrives.
/// </summary>
public sealed partial class KsDissolveSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> DissolveShader = "KsDissolve";

    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<KsDissolveVisualsComponent> _visualsQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsDissolveComponent> entity, ref ComponentStartup args)
    {
        if (!_spriteQuery.TryComp(entity, out var spriteComponent) || _visualsQuery.HasComp(entity))
            return;

        var shader = ProtoMan.Index(DissolveShader).InstanceUnique();
        var visualsComponent = new KsDissolveVisualsComponent
        {
            LocalStartTime = _gameTiming.CurTime,
            Shader = shader,
            Seed = (float)(entity.Owner.Id % 97),
        };
        AddComp(entity.Owner, visualsComponent);

        shader.SetParameter("progress", 0f);
        shader.SetParameter("seed", visualsComponent.Seed);

        // Stacks with any post-shader the sprite already has, ordered before the outlines.
        _spriteSystem.SetPostShader((entity.Owner, spriteComponent), new SpriteComponent.PostShaderArgs(KsPostShaderIds.Dissolve, shader)
        {
            Before = KsPostShaderIds.BeforeOutlines,
        });
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsDissolveComponent> entity, ref ComponentShutdown args)
    {
        if (!_visualsQuery.TryComp(entity, out var visualsComponent))
            return;

        if (!TerminatingOrDeleted(entity.Owner))
        {
            if (_spriteQuery.TryComp(entity, out var spriteComponent))
            {
                _spriteSystem.RemovePostShader((entity.Owner, spriteComponent), KsPostShaderIds.Dissolve);
                _spriteSystem.SetVisible((entity.Owner, spriteComponent), true);
            }

            RemCompDeferred<KsDissolveVisualsComponent>(entity.Owner);
        }

        visualsComponent.Shader?.Dispose();
        visualsComponent.Shader = null;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var curTime = _gameTiming.CurTime + _gameTiming.TickRemainder;

        var enumerator = EntityQueryEnumerator<KsDissolveComponent, KsDissolveVisualsComponent, SpriteComponent>();
        while (enumerator.MoveNext(out var uid, out var dissolveComponent, out var visualsComponent, out var spriteComponent))
        {
            if (visualsComponent.Shader is not { } shader)
                continue;

            var duration = dissolveComponent.Duration.TotalSeconds;
            var progress = duration > 0d
                ? (float)Math.Clamp((curTime - visualsComponent.LocalStartTime).TotalSeconds / duration, 0d, 1d)
                : 1f;

            shader.SetParameter("progress", progress);
            shader.SetParameter("seed", visualsComponent.Seed);

            // Fully dissolved: hidden until the server's deletion arrives.
            if (progress >= 1f && spriteComponent.Visible)
                _spriteSystem.SetVisible((uid, spriteComponent), false);
        }
    }
}
