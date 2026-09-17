using System.Numerics;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Physics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;

namespace Content.Client._KS14.ZLevel;

/// <summary>
///     The pre-animation half of making a transiting entity look like it is still up where it fell from.
///     <see cref="KsZLevelTransitSpriteLiftSystem"/> applies the result after the animation player has run.
/// </summary>
/// <remarks>
///     <para>
///     Each z-level below the viewer renders at a smaller eye scale, so the instant an entity crosses a floor
///         plane it would pop: smaller, and pulled towards the centre of the screen, because scaling happens
///         about the eye. This undoes exactly that pop and then eases the correction away as the entity falls.
///     </para>
///     <para>
///     At <see cref="KsZLevelTransitComponent.Height"/> 1 the entity is drawn exactly as the z-level it left
///         would have drawn it, and at 0 it is drawn exactly like any other entity resting on the z-level it
///         landed on. In between, its apparent depth is simply its real depth minus how far up it still is.
///     </para>
///     <para>
///     Sprite offset and scale are shared property - the animation player writes them too - so this has to
///         bracket the animation player rather than being applied in one place. Adding to them without
///         resetting first accumulates forever, and overwriting them outright destroys the animation.
///     </para>
/// </remarks>
public sealed partial class KsZLevelTransitSpriteSystem : EntitySystem
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsZLevelComponent> _zLevelQuery = default!;
    [Dependency] private EntityQuery<KsZLevelTransitComponent> _transitQuery = default!;
    [Dependency] private EntityQuery<KsZLevelTransitSpriteComponent> _transitSpriteQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    /// <summary>
    ///     How much of a descent the fade-in covers. Something dropping onto the viewer's own z-level is fully
    ///         transparent as it comes through the ceiling and fully opaque once this far down.
    /// </summary>
    private const float FadeInHeight = 0.5f;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(AnimationPlayerSystem));
        UpdatesOutsidePrediction = true;
    }

    /// <summary>
    ///     Captures the sprite state to put back when the transit ends, once, while the sprite is still clean.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnTransitStarted(Entity<KsZLevelTransitComponent> entity, ref KsZLevelTransitStartedEvent args)
    {
        if (!_spriteQuery.TryGetComponent(entity.Owner, out var spriteComponent))
            return;

        var transitSpriteComponent = EnsureComp<KsZLevelTransitSpriteComponent>(entity.Owner);
        transitSpriteComponent.BaseOffset = spriteComponent.Offset;
        transitSpriteComponent.BaseScale = spriteComponent.Scale;
        transitSpriteComponent.BaseDrawDepth = spriteComponent.DrawDepth;
        transitSpriteComponent.BaseColor = spriteComponent.Color;
        transitSpriteComponent.Lift = Vector2.Zero;
        transitSpriteComponent.ScaleMultiplier = 1f;
        transitSpriteComponent.AlphaMultiplier = 1f;
    }

    [SubscribeLocalEvent]
    private void OnTransitEnded(Entity<KsZLevelTransitComponent> entity, ref KsZLevelTransitEndedEvent args)
    {
        if (!_transitSpriteQuery.TryGetComponent(entity.Owner, out var transitSpriteComponent))
            return;

        if (_spriteQuery.TryGetComponent(entity.Owner, out var spriteComponent))
        {
            _spriteSystem.SetOffset((entity.Owner, spriteComponent), transitSpriteComponent.BaseOffset);
            _spriteSystem.SetScale((entity.Owner, spriteComponent), transitSpriteComponent.BaseScale);
            _spriteSystem.SetDrawDepth((entity.Owner, spriteComponent), transitSpriteComponent.BaseDrawDepth);
            _spriteSystem.SetColor((entity.Owner, spriteComponent), transitSpriteComponent.BaseColor);
        }

        RemComp(entity.Owner, transitSpriteComponent);
    }

    public override void FrameUpdate(float frameTime)
    {
        var eye = _eyeManager.CurrentEye;
        var eyeScale = eye.Scale;

        // Everything in a pass is scaled about this point, so it is what the displacement is measured from.
        var eyePosition = eye.Position.Position + eye.Offset;

        // Depths are measured from the viewer, so their own height above their own floor plane is the origin.
        var viewerUid = _playerManager.LocalEntity;
        var viewerZLevelEntity = GetViewerZLevel(out var viewerDepth);

        var enumerator = AllEntityQuery<KsZLevelTransitSpriteComponent, KsZLevelTransitComponent, SpriteComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var transitSpriteComponent, out var transitComponent, out var spriteComponent, out var transformComponent))
        {
            // Back to the clean base, so whatever the animation player writes next is the animation alone.
            _spriteSystem.SetOffset((uid, spriteComponent), transitSpriteComponent.BaseOffset);
            _spriteSystem.SetScale((uid, spriteComponent), transitSpriteComponent.BaseScale);
            _spriteSystem.SetDrawDepth(
                (uid, spriteComponent),
                transitComponent.Height > 0f
                    ? (int)Shared.DrawDepth.DrawDepth.OverMobs
                    : transitSpriteComponent.BaseDrawDepth
            );

            _spriteSystem.SetColor((uid, spriteComponent), transitSpriteComponent.BaseColor);

            transitSpriteComponent.Lift = Vector2.Zero;
            transitSpriteComponent.ScaleMultiplier = 1f;
            transitSpriteComponent.AlphaMultiplier = 1f;

            if (viewerZLevelEntity is not { } viewerZLevel ||
                transformComponent.MapUid is not { } zLevelUid ||
                !_zLevelQuery.TryGetComponent(zLevelUid, out var zLevelComponent) ||
                !_zLevelSystem.TryGetDepthBelow(viewerZLevel!, zLevelUid, out var depthBelowViewer))
                continue; // Not on a z-level the viewer can see below themselves, so no pass to compensate for.

            // Something dropping onto the viewer's own z-level comes through a ceiling that is never rendered,
            //      so it fades in instead of appearing out of nothing. Seen from a z-level above, it is just
            //      falling away down a hole that is already in view, so it stays fully opaque.
            // The viewer's own body is exempt: watching yourself dissolve as you fall is not the effect.
            if (depthBelowViewer <= 0f && uid != viewerUid)
                transitSpriteComponent.AlphaMultiplier = Math.Clamp((1f - transitComponent.Height) / FadeInHeight, 0f, 1f);

            var actualDepth = viewerDepth + depthBelowViewer;
            var apparentDepth = actualDepth - transitComponent.Height * zLevelComponent.Depth;

            // Uniform on purpose: a single ratio commutes with the eye's rotation, a per-axis one would not.
            var actualScale = KsZLevelSystem.GetDepthScale(eyeScale, actualDepth).X;
            var apparentScale = KsZLevelSystem.GetDepthScale(eyeScale, apparentDepth).X;
            if (actualScale <= 0f)
                continue;

            var scaleMultiplier = apparentScale / actualScale;
            transitSpriteComponent.ScaleMultiplier = scaleMultiplier;

            // Scaling the sprite grows it about its own origin, so it also has to move to where the shallower
            //      scale would have put it: everything is scaled about the eye, so that is (position - eye) * k.
            var worldPosition = _transformSystem.GetWorldPosition(transformComponent);
            var displacement = (worldPosition - eyePosition) * (scaleMultiplier - 1f);

            // Sprite offset is a translation in the entity's own frame, so counter-rotate to keep it world
            //      aligned. Skip that and a spinning entity's displacement orbits its own pivot.
            transitSpriteComponent.Lift = spriteComponent.NoRotation
                ? displacement
                : (-_transformSystem.GetWorldRotation(transformComponent)).RotateVec(displacement);
        }
    }

    /// <summary>
    ///     The z-level the viewer is on, and how far their eye sits above its floor plane.
    /// </summary>
    private Entity<KsZLevelComponent>? GetViewerZLevel(out float viewerDepth)
    {
        viewerDepth = 0f;

        if (_playerManager.LocalEntity is not { } viewerUid ||
            !_zLevelSystem.TryGetZLevel(viewerUid, out var viewerZLevelEntity))
            return null;

        if (_transitQuery.TryGetComponent(viewerUid, out var viewerTransitComponent))
            viewerDepth = viewerTransitComponent.Height * viewerZLevelEntity.Value.Comp.Depth;

        return viewerZLevelEntity;
    }
}
