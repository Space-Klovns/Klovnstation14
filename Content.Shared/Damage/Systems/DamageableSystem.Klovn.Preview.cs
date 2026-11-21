using Content.Shared.Damage.Components;

namespace Content.Shared.Damage.Systems;

public sealed partial class DamageableSystem
{
    /// <summary>
    ///     What <see cref="ChangeDamage"/> would deal to <paramref name="ent"/>, without dealing it: the same steps, up
    ///         to but not including the <see cref="DamageDealtEvent"/> that writes it.
    /// </summary>
    /// <remarks>
    ///     For a hit that must not touch its target here: a client's kept copy of its own shot, which the server never
    ///         corrects, so damage it dealt would be put back by the next state and dealt again by the server's
    ///         (<c>PredictedProjectileSystem.DoHit</c>). Keep it in step with <see cref="ChangeDamage"/>
    ///         (<c>KsGunHitPredictionTest.PreviewedDamageIsWhatIsDealtAndDealsNone</c>).
    ///     It raises the same <see cref="BeforeDamageChangedEvent"/> and <see cref="DamageModifyEvent"/>, the latter with
    ///         <see cref="DamageModifyEvent.KsPreviewOnly"/>: a handler of either that changes anything but the event
    ///         must not for a preview.
    /// </remarks>
    /// <returns>The damage that would be dealt; empty if none would be.</returns>
    public DamageSpecifier PreviewDamage(
        Entity<DamageableComponent?> ent,
        DamageSpecifier damage,
        bool ignoreResistances = false,
        EntityUid? origin = null,
        bool ignoreGlobalModifiers = false)
    {
        var noDamage = new DamageSpecifier();

        if (!_damageableQuery.Resolve(ent, ref ent.Comp, false) || damage.Empty)
            return noDamage;

        var before = new BeforeDamageChangedEvent(damage, origin);
        RaiseLocalEvent(ent, ref before);

        if (before.Cancelled)
            return noDamage;

        if (!ignoreResistances)
        {
            if (ent.Comp.DamageModifierSetId != null &&
                ProtoMan.Resolve(ent.Comp.DamageModifierSetId, out var modifierSet))
                damage = DamageSpecifier.ApplyModifierSet(damage, modifierSet);

            // Its handlers are not all pure (a blocking shield takes what it blocks): the flag has those only change the amount.
            var modifyEv = new DamageModifyEvent(damage, origin) { KsPreviewOnly = true };
            RaiseLocalEvent(ent, modifyEv);
            damage = modifyEv.Damage;

            if (damage.Empty)
                return noDamage;
        }

        if (!ignoreGlobalModifiers)
            damage = ApplyUniversalAllModifiers(damage);

        return damage;
    }
}
