using Content.Shared.Gravity;
using Content.Shared.StatusEffectNew;

namespace Content.Shared._KS14.Gravity;

public sealed partial class WeightlessnessStatusEffectSystem : EntitySystem
{
    [Dependency] private SharedGravitySystem _gravitySystem = default!;

    [SubscribeLocalEvent]
    private void OnStatusEffectApplied(Entity<WeightlessnessStatusEffectComponent> entity, ref StatusEffectAppliedEvent args)
    {
        _gravitySystem.RefreshWeightless(args.Target);
    }

    [SubscribeLocalEvent]
    private void OnStatusEffectRemoved(Entity<WeightlessnessStatusEffectComponent> entity, ref StatusEffectRemovedEvent args)
    {
        _gravitySystem.RefreshWeightless(args.Target);
    }

    [SubscribeLocalEvent]
    private void OnStatusEffectIsWeightless(Entity<WeightlessnessStatusEffectComponent> entity, ref StatusEffectRelayedEvent<IsWeightlessEvent> args)
    {
        var innerArgs = args.Args;
        if (innerArgs.Handled)
            return;

        innerArgs.Handled = true;
        innerArgs.IsWeightless = true;
        args.Args = innerArgs;
    }
}
