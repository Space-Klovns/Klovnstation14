using Content.Shared.Gravity;
using Content.Shared.StatusEffectNew;

namespace Content.Shared._KS14.Gravity;

public sealed class WeightlessnessStatusEffectSystem : EntitySystem
{
    [Dependency] private readonly SharedGravitySystem _gravitySystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeightlessnessStatusEffectComponent, StatusEffectAppliedEvent>(OnStatusEffectApplied);
        SubscribeLocalEvent<WeightlessnessStatusEffectComponent, StatusEffectRemovedEvent>(OnStatusEffectRemoved);

        SubscribeLocalEvent<WeightlessnessStatusEffectComponent, StatusEffectRelayedEvent<IsWeightlessEvent>>(OnStatusEffectIsWeightless);
    }

    private void OnStatusEffectApplied(Entity<WeightlessnessStatusEffectComponent> entity, ref StatusEffectAppliedEvent args)
    {
        _gravitySystem.RefreshWeightless(args.Target);
    }

    private void OnStatusEffectRemoved(Entity<WeightlessnessStatusEffectComponent> entity, ref StatusEffectRemovedEvent args)
    {
        _gravitySystem.RefreshWeightless(args.Target);
    }

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
