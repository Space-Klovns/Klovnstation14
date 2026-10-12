using Content.Server._KS14.NPC.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
/// System that updates NPC anger modifier based on damage taken.
/// </summary>
public sealed partial class NPCAngerModifierSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;

    [SubscribeLocalEvent]
    // A by-value event: there is no Entity<T> form of handler for one.
    private void OnDamageChanged(EntityUid uid, NpcAngerModifierComponent component, DamageChangedEvent args)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageableComponent))
            return;

        var damageTaken = (float) _damageable.GetTotalDamage((uid, damageableComponent));
        component.AngerModifier = Math.Clamp(damageTaken / component.DamagePerAnger, 0f, component.MaxAnger);
    }

    public float GetAngerModifier(EntityUid uid)
    {
        if (TryComp<NpcAngerModifierComponent>(uid, out var anger))
            return anger.AngerModifier;

        return 0f;
    }
}
