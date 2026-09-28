using Content.Server.NPC.Components;
using Content.Shared._KS14.Damage.Events;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Components;
using Content.Shared.Strip;
using Content.Shared.Throwing;

namespace Content.Server.NPC.Systems;

// KS14 Additions

public sealed partial class NPCRetaliationSystem : EntitySystem
{
    private void InitialiseKlovn()
    {
        // This should all warrant retaliation but it didn't, so now it does
        SubscribeLocalEvent<NPCRetaliationComponent, KsHitByJumpEvent>(KsOnHitByJump);
        SubscribeLocalEvent<NPCRetaliationComponent, ContactInteractionEvent>(KsOnContact); // KS14: ANK: contact should warrant retaliation
        SubscribeLocalEvent<NPCRetaliationComponent, KsAfterStaminaDamageEvent>(KsAfterStaminaDamage);
        SubscribeLocalEvent<NPCRetaliationComponent, KsStrippingStartedEvent>(KsAfterStrippingStarted);
    }

    private void KsOnHitByJump(Entity<NPCRetaliationComponent> ent, ref KsHitByJumpEvent args)
    {
        TryRetaliate(ent, args.ActorUid, tryWarn: false);
    }

    private void KsOnContact(Entity<NPCRetaliationComponent> ent, ref ContactInteractionEvent args)
    {
        TryRetaliate(ent, args.Other, tryWarn: true);
    }

    private void RetaliateOnThrowerIfPossible(Entity<NPCRetaliationComponent> entity, EntityUid originUid, bool tryWarn = false)
    {
        // super hardcode god
        if (TryComp<ThrownItemComponent>(originUid, out var thrownItemComponent) &&
            thrownItemComponent.Thrower is { } throwerUid)
            TryRetaliate(entity, throwerUid, tryWarn: tryWarn);
        else
            TryRetaliate(entity, originUid, tryWarn: tryWarn);
    }

    private void KsAfterStaminaDamage(Entity<NPCRetaliationComponent> ent, ref KsAfterStaminaDamageEvent args)
    {
        if (args.Damage <= 0f ||
            args.OriginUid is not { } originUid)
            return;

        RetaliateOnThrowerIfPossible(ent, originUid, tryWarn: false /* lol no */);
    }

    private void KsAfterStrippingStarted(Entity<NPCRetaliationComponent> ent, ref KsStrippingStartedEvent args)
    {
        TryRetaliate(ent, args.UserUid, tryWarn: false /* lol no */);
    }
}
