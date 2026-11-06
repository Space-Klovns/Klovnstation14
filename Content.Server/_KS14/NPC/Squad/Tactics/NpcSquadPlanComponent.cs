namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     On whoever issues a squad's orders, only from when it first has nobody with a shot until its plan's cooldown is over.
///         See <see cref="NpcSquadTacticsSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcSquadTacticsSystem))]
public sealed partial class NpcSquadPlanComponent : Component
{
    [ViewVariables]
    public NpcSquadPlan Plan = default!;
}
