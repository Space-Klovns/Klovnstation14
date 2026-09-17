using System.Numerics;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel.Physics;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.ZLevel;

/// <summary>
///     The pre-animation half of lifting a transiting entity's sprite up the screen: resets the sprite to its
///         clean base and works out this frame's lift.
///     <see cref="KsZLevelTransitSpriteLiftSystem"/> adds that lift back on after the animation player has run.
/// </summary>
/// <remarks>
///     Sprite offset is shared property — the animation player writes it too — so the lift has to bracket the
///         animation player rather than being applied in one place. Adding it without resetting first
///         accumulates forever, and overwriting it outright destroys the animation.
/// </remarks>
public sealed partial class KsZLevelTransitSpriteSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<KsZLevelTransitSpriteComponent> _transitSpriteQuery = default!;

    private float _heightOffset;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(AnimationPlayerSystem));
        UpdatesOutsidePrediction = true;

        Subs.CVar(_configurationManager, KsCCVars.ZLevelTransitHeightOffset, value => _heightOffset = value, true);
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
        transitSpriteComponent.BaseDrawDepth = spriteComponent.DrawDepth;
    }

    [SubscribeLocalEvent]
    private void OnTransitEnded(Entity<KsZLevelTransitComponent> entity, ref KsZLevelTransitEndedEvent args)
    {
        if (!_transitSpriteQuery.TryGetComponent(entity.Owner, out var transitSpriteComponent))
            return;

        if (_spriteQuery.TryGetComponent(entity.Owner, out var spriteComponent))
        {
            _spriteSystem.SetOffset((entity.Owner, spriteComponent), transitSpriteComponent.BaseOffset);
            _spriteSystem.SetDrawDepth((entity.Owner, spriteComponent), transitSpriteComponent.BaseDrawDepth);
        }

        RemComp(entity.Owner, transitSpriteComponent);
    }

    public override void FrameUpdate(float frameTime)
    {
        var enumerator = AllEntityQuery<KsZLevelTransitSpriteComponent, KsZLevelTransitComponent, SpriteComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var transitSpriteComponent, out var transitComponent, out var spriteComponent, out var transformComponent))
        {
            // Back to the clean base, so whatever the animation player writes next is the animation alone.
            _spriteSystem.SetOffset((uid, spriteComponent), transitSpriteComponent.BaseOffset);
            _spriteSystem.SetDrawDepth(
                (uid, spriteComponent),
                transitComponent.Height > 0f
                    ? (int)Shared.DrawDepth.DrawDepth.OverMobs
                    : transitSpriteComponent.BaseDrawDepth
            );

            var lift = new Vector2(0f, transitComponent.Height * _heightOffset);

            // Counter-rotate, or the lift is applied in sprite-local space and a spinning entity's height
            //      offset orbits its own pivot instead of pointing up.
            transitSpriteComponent.Lift = spriteComponent.NoRotation
                ? lift
                : (-_transformSystem.GetWorldRotation(transformComponent)).RotateVec(lift);
        }
    }
}
