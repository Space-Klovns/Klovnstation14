using Content.Client._KS14.Graphics;
using Content.Shared._KS14.WaveDistortion;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Client._KS14.WaveDistortion;

/*
    The original version of this source code was ported from
        https://github.com/crystallpunk-14/crystall-punk-14/ at commit 5b6108377e40235c768be3ac6ffadb37a085f441
*/

public sealed partial class KsWaveDistortionSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    [Dependency] private EntityQuery<KsMapWaveDistortionModifierComponent> _modifierQuery = default!;

    private static readonly ProtoId<ShaderPrototype> ShaderId = "KsWaveDistortion";
    private ShaderInstance _shader = default!;

    public override void Initialize()
    {
        base.Initialize();

        _shader = ProtoMan.Index(ShaderId).InstanceUnique();
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsWaveDistortionComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.Offset = _random.NextFloat(0, 1000);
        SetShader(entity.Owner, true);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsWaveDistortionComponent> entity, ref ComponentShutdown args)
    {
        SetShader(entity.Owner, false);
    }

    private void SetShader(Entity<SpriteComponent?> entity, bool enabled)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        if (!enabled)
        {
            _spriteSystem.RemovePostShader((entity.Owner, entity.Comp), KsPostShaderIds.WaveDistortion);
            return;
        }

        _spriteSystem.SetPostShader((entity.Owner, entity.Comp),
            new SpriteComponent.PostShaderArgs(KsPostShaderIds.WaveDistortion, _shader)
            {
                GetScreenTexture = true,
                RaiseShaderEvent = true,
                Before = KsPostShaderIds.BeforeOutlines,
            });
    }

    [SubscribeLocalEvent]
    private void OnBeforeShaderPost(Entity<KsWaveDistortionComponent> entity, ref BeforePostShaderRenderEvent args)
    {
        // The event now fires once per post-shader entry on the sprite, so only answer for ours.
        if (args.Id != KsPostShaderIds.WaveDistortion)
            return;

        var speedModifier = 1f;

        if (Transform(entity.Owner).MapUid is { } mapUid &&
            _modifierQuery.TryGetComponent(mapUid, out var modifierComponent))
            speedModifier = modifierComponent.Multiplier;

        args.Shader.SetParameter("speed", entity.Comp.Speed * speedModifier);
        args.Shader.SetParameter("dis", entity.Comp.Distortion);
        args.Shader.SetParameter("offset", entity.Comp.Offset);
    }
}
