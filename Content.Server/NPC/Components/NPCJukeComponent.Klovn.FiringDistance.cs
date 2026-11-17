// KS14: added in this fork
namespace Content.Server.NPC.Components;

public sealed partial class NPCJukeComponent
{
    /// <summary>
    ///     The furthest a ranged NPC backs off to shoot, if capped. See <c>JukeOperator.MaxFiringDistanceKey</c>.
    /// </summary>
    [DataField]
    public float? KsMaxFiringDistance;
}
