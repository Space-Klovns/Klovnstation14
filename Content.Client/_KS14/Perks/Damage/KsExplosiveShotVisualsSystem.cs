using Content.Shared._KS14.Perks.Damage;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Perks.Damage;

/// <summary>
///     Draws an explosive round in its tracer colour, so it can be told from the rest of the magazine.
/// </summary>
public sealed partial class KsExplosiveShotVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _spriteSystem = default!;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsExplosiveShotComponent> entity, ref ComponentStartup args)
    {
        _spriteSystem.SetColor(entity.Owner, entity.Comp.TracerColor);
    }

    [SubscribeLocalEvent]
    private void OnAfterState(Entity<KsExplosiveShotComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        _spriteSystem.SetColor(entity.Owner, entity.Comp.TracerColor);
    }
}
