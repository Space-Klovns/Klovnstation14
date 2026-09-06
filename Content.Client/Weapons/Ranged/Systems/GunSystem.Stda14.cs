using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Spawners;
using SharedGunSystem = Content.Shared.Weapons.Ranged.Systems.SharedGunSystem;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem : SharedGunSystem
{
    private void DoMuzzleEffect(EntityUid gunUid, EntityUid animationUid, float baseLifetime)
    {
        // KS14: Use despawn lifetime from the *affected entity*, not the gun FFS wtf is this
        if (TryComp<TimedDespawnComponent>(animationUid, out var despawn))
            baseLifetime = despawn.Lifetime;

        var anim = new Animation()
        {
            Length = TimeSpan.FromSeconds(baseLifetime),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(1f), 0),
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(0f), baseLifetime)
                    }
                }
            }
        };

        _animPlayer.Play(animationUid, anim, "muzzle-flash");
    }
}
