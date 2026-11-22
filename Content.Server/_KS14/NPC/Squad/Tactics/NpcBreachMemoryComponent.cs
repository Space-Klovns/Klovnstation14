namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Where charges have gone off on this grid lately, for <see cref="NpcSquadTacticsSystem"/> to blow its next hole
///         somewhere the defenders are not already watching (<see cref="NpcSquadTacticsSettings.BreachScoring"/>). Shared
///         by every squad: a hole is a hole, whoever made it.
/// </summary>
[RegisterComponent, Access(typeof(NpcSquadTacticsSystem))]
public sealed partial class NpcBreachMemoryComponent : Component
{
    /// <summary>
    ///     Each charge's tile and when it went off, oldest first. Old ones are dropped when the next is added.
    /// </summary>
    [ViewVariables]
    public List<(Vector2i Tile, TimeSpan At)> Breaches = new();
}
