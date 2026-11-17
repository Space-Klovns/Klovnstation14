using Content.Client.Rotation;
using Content.Shared._KS14.Alligators;
using Content.Shared.Buckle.Components;
using Content.Shared.Rotation;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Alligators;

/// <summary>
///     Draws a death roll's victim feet-first in the alligator's jaws and rolls them over and over,
///     ignoring the facing and lying angle they would otherwise have, and keeps them drawn under the alligator.
/// </summary>
public sealed partial class KsDeathRollVictimVisualsSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _animationPlayerSystem = default!;
    [Dependency] private AppearanceSystem _appearanceSystem = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private RotationVisualizerSystem _rotationVisualizerSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    /// <summary>
    ///     The key <see cref="RotationVisualizerSystem"/> plays its stand-up and lie-down animation under.
    /// </summary>
    private const string RotateAnimationKey = "rotate";

    private static readonly Direction[] RollFacings = [Direction.South, Direction.East, Direction.North, Direction.West];

    public override void FrameUpdate(float frameTime)
    {
        var curTime = _gameTiming.CurTime + _gameTiming.TickRemainder;
        var eyeRotation = _eyeManager.CurrentEye.Rotation;

        var query = EntityQueryEnumerator<KsDeathRollVictimComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var component, out var spriteComponent))
        {
            if (component.Alligator is not { } alligatorUid || !Exists(alligatorUid))
                continue;

            // The lying-down animation would fight over the rotation for as long as it runs.
            if (_animationPlayerSystem.HasRunningAnimation(uid, RotateAnimationKey))
                _animationPlayerSystem.Stop(uid, RotateAnimationKey);

            // The victim's sprite ignores entity rotation, so its own rotation is an angle on screen.
            // An upright sprite has its head to the north: turn that to point the way the alligator faces.
            var alligatorFacing = _transformSystem.GetWorldRotation(alligatorUid) + eyeRotation;
            _spriteSystem.SetRotation((uid, spriteComponent), (alligatorFacing + Math.PI).Reduced());

            var step = component.StepTime > TimeSpan.Zero
                ? (long)Math.Floor((curTime - component.StartTime) / component.StepTime)
                : 0;
            spriteComponent.DirectionOverride = RollFacings[(int)(((step % RollFacings.Length) + RollFacings.Length) % RollFacings.Length)];
            spriteComponent.EnableDirectionOverride = true;

            // Both are mobs, so the victim would be drawn over the alligator whenever they are lower on screen.
            // Buckling only puts them under it for a north-facing strap, and undoes that on every other turn.
            if (TryComp<SpriteComponent>(alligatorUid, out var alligatorSprite))
            {
                // Buckling may have lowered the depth already: keep the depth it saved, not the lowered one.
                component.OriginalDrawDepth ??= CompOrNull<BuckleComponent>(uid)?.OriginalDrawDepth ?? spriteComponent.DrawDepth;
                _spriteSystem.SetDrawDepth((uid, spriteComponent), alligatorSprite.DrawDepth - 1);
            }
        }
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsDeathRollVictimComponent> victim, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(victim.Owner) || !TryComp<SpriteComponent>(victim.Owner, out var spriteComponent))
            return;

        spriteComponent.EnableDirectionOverride = false;

        if (victim.Comp.OriginalDrawDepth is { } originalDrawDepth)
        {
            _spriteSystem.SetDrawDepth((victim.Owner, spriteComponent), originalDrawDepth);
            victim.Comp.OriginalDrawDepth = null;
        }

        // Hand the rotation back: turn to whatever angle standing or lying calls for now.
        if (!TryComp<RotationVisualsComponent>(victim.Owner, out var rotationVisualsComponent))
        {
            _spriteSystem.SetRotation((victim.Owner, spriteComponent), Angle.Zero);
            return;
        }

        _appearanceSystem.TryGetData<RotationState>(victim.Owner, RotationVisuals.RotationState, out var state);
        var rotation = state == RotationState.Horizontal
            ? rotationVisualsComponent.HorizontalRotation
            : rotationVisualsComponent.VerticalRotation;
        _rotationVisualizerSystem.AnimateSpriteRotation(victim.Owner, spriteComponent, rotation, rotationVisualsComponent.AnimationTime);
    }
}
