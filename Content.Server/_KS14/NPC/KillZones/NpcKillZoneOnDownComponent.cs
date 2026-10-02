namespace Content.Server._KS14.NPC.KillZones;

/// <summary>
///     Marks a kill zone where this NPC goes down - critical, or dead - for NPCs of its factions to avoid. See
///         <see cref="NpcKillZoneSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NpcKillZoneOnDownComponent : Component
{
    /// <summary>
    ///     How far out the zone reaches, in steps across the floor - never through walls, and no further than the
    ///         doorways of the room or corridor it is in.
    /// </summary>
    [DataField]
    public int Reach = 3;

    /// <summary>
    ///     How long it is avoided for.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(90);
}
