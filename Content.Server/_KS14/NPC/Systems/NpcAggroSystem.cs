using Content.Server.NPC.HTN;
using Content.Shared.Damage.Systems;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
/// NPC aggro: the first player to damage the mob locks themselves
/// in as Target and sets the Aggroed flag. Proximity aggro is handled
/// separately by RangedBossTargeting.
/// </summary>
public sealed partial class NpcAggroSystem : EntitySystem
{
    [SubscribeLocalEvent]
    // A by-value event: there is no Entity<T> form of handler for one.
    private void OnDamaged(EntityUid uid, NpcAggroComponent component, DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta is not { } delta || delta.GetTotal() <= 0)
            return;

        if (args.Origin is not { } originUid || originUid == uid)
            return;

        Aggro((uid, component), originUid);
    }

    /// <summary>
    /// Marks the mob aggroed and locks the attacker as its target.
    /// Idempotent - first aggressor wins.
    /// </summary>
    public void Aggro(Entity<NpcAggroComponent?> entity, EntityUid targetUid)
    {
        if (!Resolve(entity.Owner, ref entity.Comp) || entity.Comp.Aggroed)
            return;

        entity.Comp.Aggroed = true;

        if (TryComp<HTNComponent>(entity, out var htnComponent))
        {
            htnComponent.Blackboard.SetValue("Aggroed", true);
            htnComponent.Blackboard.SetValue("Target", targetUid);
        }
    }

    public bool IsAggroed(EntityUid uid)
    {
        return TryComp<NpcAggroComponent>(uid, out var comp) && comp.Aggroed;
    }
}

[RegisterComponent]
public sealed partial class NpcAggroComponent : Component
{
    [DataField("aggroed")]
    public bool Aggroed;
}
