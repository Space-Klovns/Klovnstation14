using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     On an NPC that has been holding fire for a friend in its way. See
///         <see cref="NpcSquadFireLaneSystem.NoteHeldFire"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcSquadFireLaneSystem))]
public sealed partial class NpcHoldingFireComponent : Component
{
    /// <summary>
    ///     Since when, standing about where <see cref="Coordinates"/> are.
    /// </summary>
    [AutoPausedField, ViewVariables]
    public TimeSpan Since;

    /// <summary>
    ///     When it last held a shot. It stops counting once it has not for a moment.
    /// </summary>
    [AutoPausedField, ViewVariables]
    public TimeSpan LastHeldAt;

    /// <summary>
    ///     Where it was when it started holding fire. Moving on starts the count again.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates Coordinates;
}
