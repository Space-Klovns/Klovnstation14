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
}
