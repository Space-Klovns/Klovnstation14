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

public sealed class KsWaveDistortionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    [Dependency] private readonly EntityQuery<KsMapWaveDistortionModifierComponent> _modifierQuery = default!;

    private static readonly ProtoId<ShaderPrototype> ShaderId = "KsWaveDistortion";
    private ShaderInstance _shader = default!;

    public override void Initialize()
    {
        base.Initialize();

        _shader = _prototypeManager.Index(ShaderId).InstanceUnique();

        SubscribeLocalEvent<KsWaveDistortionComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KsWaveDistortionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<KsWaveDistortionComponent, BeforePostShaderRenderEvent>(OnBeforeShaderPost);
    }

    private void OnStartup(Entity<KsWaveDistortionComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.Offset = _random.NextFloat(0, 1000);
        SetShader(entity.Owner, true);
    }

    private void OnShutdown(Entity<KsWaveDistortionComponent> entity, ref ComponentShutdown args)
    {
        SetShader(entity.Owner, false);
    }

    private void SetShader(Entity<SpriteComponent?> entity, bool enabled)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        entity.Comp.PostShader = enabled ? _shader : null;
        entity.Comp.GetScreenTexture = enabled;
        entity.Comp.RaiseShaderEvent = enabled;
    }

    private void OnBeforeShaderPost(Entity<KsWaveDistortionComponent> entity, ref BeforePostShaderRenderEvent args)
    {
        var speedModifier = 1f;

        if (Transform(entity.Owner).MapUid is { } mapUid &&
            _modifierQuery.TryGetComponent(mapUid, out var modifierComponent))
            speedModifier = modifierComponent.Multiplier;

        _shader.SetParameter("speed", entity.Comp.Speed * speedModifier);
        _shader.SetParameter("dis", entity.Comp.Distortion);
        _shader.SetParameter("offset", entity.Comp.Offset);
    }
}
