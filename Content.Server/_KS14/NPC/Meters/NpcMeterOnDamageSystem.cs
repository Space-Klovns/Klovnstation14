using Content.Shared.Damage.Systems;

namespace Content.Server._KS14.NPC.Meters;

/// <summary>
///     Moves <see cref="NpcMeterOnDamageComponent"/> meters with the damage their NPC takes and heals.
/// </summary>
public sealed partial class NpcMeterOnDamageSystem : EntitySystem
{
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;

    [SubscribeLocalEvent]
    // A by-value event: there is no Entity<T> form of handler for one.
    private void OnDamageChanged(EntityUid uid, NpcMeterOnDamageComponent component, DamageChangedEvent args)
    {
        if (args.DamageDelta is not { } damageDelta)
            return;

        _npcMeterSystem.Add(uid, component.Meter, (float)damageDelta.GetTotal() * component.PerDamage);
    }
}
