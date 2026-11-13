namespace Content.Server._KS14.NPC.Dangers;

/// <summary>
///     The sorts of danger a tile can pose to an NPC walking onto it. Flags, so that a tile can pose several and an NPC can
///         avoid several, and a chunk's worth can be read at a glance. See <see cref="NpcAvoidsTileDangerComponent"/>.
/// </summary>
[Flags]
public enum NpcTileDangerKind : byte
{
    None = 0,

    /// <summary>
    ///     On fire: a chemical fire, or a gas fire.
    /// </summary>
    Fire = 1 << 0,

    /// <summary>
    ///     Within reach of a primed explosive's blast, or of where one in the air will land. See
    ///         <c>PathfindingSystem.Klovn.Blast.cs</c>.
    /// </summary>
    Blast = 1 << 1,

    /// <summary>
    ///     Within range of a primed flash - a flashbang, a stinger's flash - or of where one in the air will land. Not a danger
    ///         to one the flash cannot blind (eye protection, no eyes): see <c>PathfindingSystem.GetAvoidedDangers</c>.
    /// </summary>
    Flash = 1 << 2,
}
