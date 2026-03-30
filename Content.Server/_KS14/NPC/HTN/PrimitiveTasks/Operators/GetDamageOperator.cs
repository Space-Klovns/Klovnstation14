using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Damage.Systems;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Sets the value of the target key to the health of the NPC.
/// </summary>
public sealed partial class GetDamageOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;

    [DataField(required: true)] public string Key = "Damage";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _) => (true, new Dictionary<string, object>()
        {
            {Key, (float)_damageableSystem.GetTotalDamage(blackboard.GetValueOrDefault<EntityUid>(NPCBlackboard.Owner, _entityManager)!)}
        });
}
