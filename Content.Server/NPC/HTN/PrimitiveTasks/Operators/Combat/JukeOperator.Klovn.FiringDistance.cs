// KS14: added in this fork
using Content.Server.NPC.Components;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Combat;

public sealed partial class JukeOperator
{
    /// <summary>
    ///     If set, the float at this blackboard key caps how far a ranged NPC backs off to shoot, however accurate its
    ///         gun: without it, a precise gun keeps the NPC at four and a half times its ideal range, sixteen tiles and
    ///         more for a rifle.
    /// </summary>
    [DataField]
    public string? MaxFiringDistanceKey;

    private void KsApplyMaxFiringDistance(NPCBlackboard blackboard, NPCJukeComponent jukeComponent)
    {
        jukeComponent.KsMaxFiringDistance = MaxFiringDistanceKey != null &&
            blackboard.TryGetValue<float>(MaxFiringDistanceKey, out var maxFiringDistance, _entManager)
                ? maxFiringDistance
                : null;
    }
}
