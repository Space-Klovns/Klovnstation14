namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     On whoever issues a squad's orders, from when a plan starts (nobody has a shot, or the squad is too hurt to fight on) until its cooldown is over.
///         See <see cref="NpcSquadTacticsSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcSquadTacticsSystem))]
public sealed partial class NpcSquadPlanComponent : Component
{
    [ViewVariables]
    public NpcSquadPlan Plan = default!;
}
