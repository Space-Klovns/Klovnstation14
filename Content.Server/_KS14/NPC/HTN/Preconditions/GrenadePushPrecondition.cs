using Content.Server._KS14.NPC.Grenades;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met while the owner should be going in on a grenade that just went off (see <see cref="NpcGrenadeSystem"/>).
/// </summary>
public sealed partial class GrenadePushPrecondition : KsInvertiblePrecondition
{
    [Dependency] private NpcGrenadeSystem _npcGrenadeSystem = default!;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        return _npcGrenadeSystem.TryGetPush(blackboard.GetOwner(), out _);
    }
}
