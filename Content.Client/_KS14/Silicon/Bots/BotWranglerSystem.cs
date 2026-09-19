using Content.Client._KS14.Graphics;
using Content.Shared._KS14.Silicons.Bots;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Silicon.Bots;

public sealed partial class BotWranglerSystem : SharedBotWranglerSystem
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "KsBotWranglingSelectionOutline";

    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    private ShaderInstance _shader = default!;

    public override void Initialize()
    {
        base.Initialize();

        _shader = ProtoMan.Index(ShaderId).InstanceUnique();
    }

    protected override void AfterActivelyWrangledBotShutdown(Entity<ActivelyWrangledBotComponent> entity)
    {
        base.AfterActivelyWrangledBotShutdown(entity);
        SetEnabled(entity.Owner, false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var eqe = EntityQueryEnumerator<ActivelyWrangledBotComponent, SpriteComponent>();
        while (eqe.MoveNext(out var uid, out var activelyWrangledBotComponent, out var spriteComponent))
            SetEnabled((uid, spriteComponent), activelyWrangledBotComponent.UserUid == _playerManager.LocalEntity);
    }

    private void SetEnabled(Entity<SpriteComponent?> entity, bool value)
    {
        if (!Resolve(entity, ref entity.Comp))
            return;

        if (!value)
        {
            _spriteSystem.RemovePostShader((entity.Owner, entity.Comp), KsPostShaderIds.BotWranglerOutline);
            return;
        }

        // This runs every tick, and SetPostShader unconditionally flags the sprite's shader order
        // dirty, which would force a re-sort every frame. Only set it when it isn't already there.
        if (_spriteSystem.HasPostShader((entity.Owner, entity.Comp), KsPostShaderIds.BotWranglerOutline))
            return;

        _spriteSystem.SetPostShader((entity.Owner, entity.Comp),
            new SpriteComponent.PostShaderArgs(KsPostShaderIds.BotWranglerOutline, _shader)
            {
                After = KsPostShaderIds.AfterBaseEffects,
            });
    }
}
