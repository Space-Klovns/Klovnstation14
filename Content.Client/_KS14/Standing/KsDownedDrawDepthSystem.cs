using Content.Shared.Standing;
using Robust.Client.GameObjects;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._KS14.Standing;

/// <summary>
///     Draws a mob that is lying down at <see cref="DownedDrawDepth"/>, under what lies on the floor at
///         <see cref="DrawDepth.BelowMobs"/> (barbed wire, bullets) but still over most floor clutter, and puts it back
///         when it stands. Standing mobs draw over all of those.
/// </summary>
/// <remarks>
///     Follows the networked <see cref="StandingStateComponent.Standing"/> through the appearance change that
///         <see cref="StandingStateSystem.Down"/> and <see cref="StandingStateSystem.Stand"/> always make, since the
///         client raises no downed or stood event for a state it only receives. Only a lowered depth is put back, and
///         only if nothing else changed it in the meantime (death, a buckle).
/// </remarks>
public sealed partial class KsDownedDrawDepthSystem : EntitySystem
{
    /// <summary>
    ///     One step below <see cref="DrawDepth.BelowMobs"/>: the sprite sort breaks a tie by render order, which the
    ///         hover outline resets, so sharing barbed wire's depth would draw a body over the wire it lies in half the time.
    /// </summary>
    public const DrawDepth DownedDrawDepth = DrawDepth.Items;

    [Dependency] private SpriteSystem _spriteSystem = default!;

    [SubscribeLocalEvent]
    private void OnAppearanceChange(Entity<StandingStateComponent> entity, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } spriteComponent)
            return;

        var spriteEntity = new Entity<SpriteComponent>(entity.Owner, spriteComponent);
        if (!entity.Comp.Standing)
            Lower(spriteEntity);
        else
            Restore(spriteEntity);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<StandingStateComponent> entity, ref ComponentShutdown args)
    {
        if (!Terminating(entity) && TryComp<SpriteComponent>(entity, out var spriteComponent))
            Restore((entity.Owner, spriteComponent));
    }

    private void Lower(Entity<SpriteComponent> spriteEntity)
    {
        if (HasComp<KsDownedDrawDepthComponent>(spriteEntity) || spriteEntity.Comp.DrawDepth <= (int)DownedDrawDepth)
            return;

        var downedComponent = AddComp<KsDownedDrawDepthComponent>(spriteEntity);
        downedComponent.OriginalDrawDepth = spriteEntity.Comp.DrawDepth;
        _spriteSystem.SetDrawDepth(spriteEntity.AsNullable(), (int)DownedDrawDepth);
    }

    private void Restore(Entity<SpriteComponent> spriteEntity)
    {
        if (!TryComp<KsDownedDrawDepthComponent>(spriteEntity, out var downedComponent))
            return;

        if (spriteEntity.Comp.DrawDepth == (int)DownedDrawDepth)
            _spriteSystem.SetDrawDepth(spriteEntity.AsNullable(), downedComponent.OriginalDrawDepth);

        RemComp<KsDownedDrawDepthComponent>(spriteEntity);
    }
}
