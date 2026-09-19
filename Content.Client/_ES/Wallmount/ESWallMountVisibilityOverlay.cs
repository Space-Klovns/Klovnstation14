using Content.Client._ES.Wallmount.Systems;
using Content.Client._KS14.ArcVisibility; // KS14
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._ES.Wallmount;

/// <summary>
///     Renders wallmount visibility based on their facing direction and position relative to the center of a viewport's eye.
///     This abuses the fact that sprite render commands (like setting visibility) are not batched in any way, and we can
///     just set the visibility to something else mid-render
/// </summary>
public sealed partial class ESWallMountVisibilityOverlay : Overlay
{
    [Dependency] private IEntityManager _ent = default!;
    private readonly TransformSystem _xform;
    private readonly SpriteSystem _sprite;
    private readonly ESWallMountTreeSystem _tree;
    private readonly ArcVisibilitySystem _arcVisibilitySystem; // KS14

    public ESWallMountVisibilityOverlay()
    {
        IoCManager.InjectDependencies(this);

        _xform = _ent.EntitySysManager.GetEntitySystem<TransformSystem>();
        _sprite = _ent.EntitySysManager.GetEntitySystem<SpriteSystem>();
        _tree = _ent.EntitySysManager.GetEntitySystem<ESWallMountTreeSystem>();
        _arcVisibilitySystem = _ent.EntitySysManager.GetEntitySystem<ArcVisibilitySystem>(); // KS14
    }

    // b4 entities so we can modify their visibility and such
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null)
            return;

        // KS14: the screen-space math this used to do inline lives in ArcVisibilitySystem now, so stains can share it
        if (!_arcVisibilitySystem.TryGetEyeState(args.Viewport, out var eyeState))
            return;

        var entities = _tree.QueryAabb(args.MapId, args.WorldBounds);

        foreach (var entry in entities)
        {
            var (wallmount, xform) = entry;
            var uid = entry.Uid; // this uses component.Owner.. oh well

            if (!_ent.TryGetComponent<SpriteComponent>(uid, out var sprite))
                continue;

            if (!args.Viewport.Eye.DrawFov ||
                !args.Viewport.Eye.DrawLight /* KS14 */)
            {
                _sprite.SetColor((uid, sprite), sprite.Color.WithAlpha(entry.Component.OriginalAlpha));
                _sprite.SetVisible((uid, sprite), true);
                continue;
            }

            // shouldnt be here in the query to begin with bc of addtotree check but if it is we ignore it
            if (wallmount.Arc >= Math.Tau)
                continue;

            // KS14 start: a wallmount on something you can see straight through has no hidden side to speak of
            if (!_arcVisibilitySystem.IsOnOccludedTile(xform))
            {
                _sprite.SetColor((uid, sprite), sprite.Color.WithAlpha(entry.Component.OriginalAlpha));
                _sprite.SetVisible((uid, sprite), true);
                continue;
            }
            // KS14 end

            var (pos, rot) = _xform.GetWorldPositionRotation(xform);

            // KS14 start: fade out towards the edges of the arc instead of just popping in and out
            var visible = _arcVisibilitySystem.TryGetArcAlpha(
                eyeState,
                pos,
                rot,
                wallmount.Direction,
                wallmount.Arc,
                entry.Component.OriginalAlpha,
                out var alpha
            );

            if (visible)
            {
                if (sprite.Visible != visible)
                    entry.Component.OriginalAlpha = sprite.Color.A;

                _sprite.SetColor((uid, sprite), sprite.Color.WithAlpha(alpha));
            }
            else if (sprite.Visible != visible)
                _sprite.SetColor((uid, sprite), sprite.Color.WithAlpha(entry.Component.OriginalAlpha));
            // KS14 end

            _sprite.SetVisible((uid, sprite), visible);
        }
    }
}
