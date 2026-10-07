using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     The latest disturbance a squad (or an NPC on its own) has heard of and not yet dealt with: somewhere something
///         happened, with nobody seen. A cautious squad hunts it rather than walking up to it. See
///         <see cref="NpcSquadTacticsSystem.NoteDisturbance"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcSquadTacticsSystem))]
public sealed partial class NpcPendingDisturbanceComponent : Component
{
    /// <summary>
    ///     Where it happened, relative to the grid or map.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates Coordinates;

    [ViewVariables]
    public TimeSpan ReportedAt;
}
