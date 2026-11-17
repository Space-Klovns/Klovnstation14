using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Grenades;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Grenades;

/// <summary>
///     Writes where the owner should be going in on (see <see cref="NpcGrenadeSystem"/>) to the blackboard. Fails if nowhere.
/// </summary>
public sealed partial class GetGrenadePushOperator : HTNOperator
{
    [Dependency] private NpcGrenadeSystem _npcGrenadeSystem = default!;

    [DataField]
    public string CoordinatesKey = "GrenadePushCoordinates";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!_npcGrenadeSystem.TryGetPush(blackboard.GetOwner(), out var coordinates))
            return (false, null);

        return (true, new Dictionary<string, object> { { CoordinatesKey, coordinates } });
    }
}
