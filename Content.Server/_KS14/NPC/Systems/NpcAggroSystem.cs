using Content.Server.NPC.HTN;
using Content.Shared.Damage.Systems;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
/// NPC aggro: the first player to damage the mob locks themselves
/// in as Target and sets the Aggroed flag. Proximity aggro is handled
/// separately by RangedBossTargeting.
/// </summary>
public sealed class NpcAggroSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcAggroComponent, DamageChangedEvent>(OnDamaged);
    }

    private void OnDamaged(EntityUid uid, NpcAggroComponent comp, DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta is not { } delta || delta.GetTotal() <= 0)
            return;

        var source = args.Origin;
        if (source == null || source == uid)
            return;

        Aggro(uid, source.Value, comp);
    }

    /// <summary>
    /// Marks the mob aggroed and locks the attacker as its target.
    /// Idempotent - first aggressor wins.
    /// </summary>
    public void Aggro(EntityUid uid, EntityUid target, NpcAggroComponent? comp = null)
    {
        if (!Resolve(uid, ref comp) || comp.Aggroed)
            return;

        comp.Aggroed = true;

        if (TryComp<HTNComponent>(uid, out var htn))
        {
            htn.Blackboard.SetValue("Aggroed", true);
            htn.Blackboard.SetValue("Target", target);
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
