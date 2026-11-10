using Content.Shared._KS14.Flash; // KS14
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Prototypes;

namespace Content.Shared.Eye.Blinding.Systems;

public sealed partial class BlindnessSystem : EntitySystem
{
    public static readonly EntProtoId BlindingStatusEffect = "StatusEffectBlindness";

    [Dependency] private BlindableSystem _blindableSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlindnessStatusEffectComponent, StatusEffectAppliedEvent>(OnApplied);
        SubscribeLocalEvent<BlindnessStatusEffectComponent, StatusEffectRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<BlindnessStatusEffectComponent, StatusEffectRelayedEvent<CanSeeAttemptEvent>>(OnBlindTrySee);
        SubscribeLocalEvent<BlindnessStatusEffectComponent, StatusEffectRelayedEvent<KsCanFlashEvent>>(OnCanFlash); // KS14: was FlashAttemptEvent
    }

    private void OnApplied(Entity<BlindnessStatusEffectComponent> ent, ref StatusEffectAppliedEvent args)
    {
        _blindableSystem.UpdateIsBlind(args.Target);
    }

    private void OnRemoved(Entity<BlindnessStatusEffectComponent> ent, ref StatusEffectRemovedEvent args)
    {
        _blindableSystem.UpdateIsBlind(args.Target);
    }

    private void OnBlindTrySee(Entity<BlindnessStatusEffectComponent> ent, ref StatusEffectRelayedEvent<CanSeeAttemptEvent> args)
    {
        var ev = args.Args;
        ev.Cancel();
        args.Args = ev;
    }

    private void OnCanFlash(Entity<BlindnessStatusEffectComponent> ent, ref StatusEffectRelayedEvent<KsCanFlashEvent> args) // KS14: was FlashAttemptEvent
    {
        var ev = args.Args;
        ev.Cancelled = true;
        args.Args = ev;
    }
}
