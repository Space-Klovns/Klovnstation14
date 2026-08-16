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
    [Dependency] private IPrototypeManager _protoMan = default!;

    private ShaderInstance _shader = default!;

    public override void Initialize()
    {
        base.Initialize();

        _shader = _protoMan.Index(ShaderId).InstanceUnique();
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

        entity.Comp.PostShader = value ? _shader : null;
    }
}
