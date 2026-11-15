using Content.Server._KS14.NPC.Lights;
using Content.Server.NPC;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met while the owner has a light to shoot out: one <see cref="NpcLightShooterSystem"/> has picked, that is still lit and
///         whose time is not up. Also the recheck of the "Shoot out a light" task, which ends it the moment it stops being so.
/// </summary>
public sealed partial class LightPickPrecondition : KsInvertiblePrecondition
{
    [Dependency] private NpcLightShooterSystem _npcLightShooterSystem = default!;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        return _npcLightShooterSystem.TryGetPick(blackboard.GetOwner(), out _);
    }
}
