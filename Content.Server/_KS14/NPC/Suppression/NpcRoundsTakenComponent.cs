namespace Content.Server._KS14.NPC.Suppression;

/// <summary>
///     On something NPCs have fired at to break it (a locker, a window): how many rounds it has taken from them so far, all
///         NPCs together, for the round. Only lockers are given up on by it (<c>RoundsTakenPrecondition</c>,
///         <c>NpcSquadTacticsSettings.SearchBreakLockerRounds</c>); glass has a time limit of its own. Goes with what it is on.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcSuppressiveFireSystem))]
public sealed partial class NpcRoundsTakenComponent : Component
{
    [ViewVariables]
    public int Rounds;
}
