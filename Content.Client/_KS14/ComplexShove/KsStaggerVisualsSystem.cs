using System.Numerics;
using Content.Shared._KS14.ComplexShove;
using Content.Shared.StatusEffectNew;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._KS14.ComplexShove;

/// <summary>
///     Sways the sprite of whoever has a <see cref="KsStaggerVisualsComponent"/> status effect, for as long as they
///         have it.
/// </summary>
public sealed partial class KsStaggerVisualsSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    [SubscribeLocalEvent]
    private void OnApplied(Entity<KsStaggerVisualsComponent> effect, ref StatusEffectAppliedEvent args)
    {
        if (!TryComp<SpriteComponent>(args.Target, out var sprite))
            return;

        // Staggered again while still swaying: the offset to go back to is the one remembered the first time.
        if (!EnsureComp<KsStaggerSwayComponent>(args.Target, out var sway))
            sway.BaseOffset = sprite.Offset;

        sway.Amplitude = effect.Comp.Amplitude;
        sway.Period = effect.Comp.Period;
    }

    [SubscribeLocalEvent]
    private void OnRemoved(Entity<KsStaggerVisualsComponent> effect, ref StatusEffectRemovedEvent args)
    {
        if (TerminatingOrDeleted(args.Target) || !TryComp<KsStaggerSwayComponent>(args.Target, out var sway))
            return;

        if (TryComp<SpriteComponent>(args.Target, out var sprite))
            _spriteSystem.SetOffset((args.Target, sprite), sway.BaseOffset);

        // Nothing enumerates these while this runs, and the next stagger must find it gone to remember the offset afresh.
        RemComp(args.Target, sway);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Real time, not the simulation's: this is only drawn, and must be smooth between ticks.
        var seconds = _gameTiming.RealTime.TotalSeconds;
        var query = EntityQueryEnumerator<KsStaggerSwayComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var sway, out var sprite))
        {
            if (sway.Period <= TimeSpan.Zero)
                continue;

            var swing = MathF.Sin((float)(seconds / sway.Period.TotalSeconds * Math.Tau)) * sway.Amplitude;
            _spriteSystem.SetOffset((uid, sprite), sway.BaseOffset + new Vector2(swing, 0f));
        }
    }
}
