using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;

namespace Content.Server._KS14.NPC.Preconditions;

[DataDefinition]
public sealed partial class DamageThresholdPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    [DataField("damageThreshold")] public FixedPoint2 DamageThreshold = 100;
    [DataField("checkAnyType")] public bool CheckAnyType = true;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entMan.TryGetComponent<DamageableComponent>(owner, out var damageableComp))
            return false;

        // Use GetPositiveDamage to get only positive damage values (non-deprecated)
        var positiveDamage = _damageable.GetPositiveDamage((owner, damageableComp));

        if (CheckAnyType)
        {
            // Check if any damage type exceeds threshold
            foreach (var (type, amount) in positiveDamage.DamageDict)
            {
                if (amount >= DamageThreshold)
                    return true;
            }
            return false;
        }
        else
        {
            // Check total positive damage
            return positiveDamage.GetTotal() >= DamageThreshold;
        }
    }
}
