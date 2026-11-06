namespace Content.Server._KS14.NPC.Dangers;

/// <summary>
///     Has an NPC's paths steer clear of tiles posing the sorts of danger listed here. It goes round them where it can, and
///         crosses them if that is the only way, but never stops on one: steering stops short of a danger at the end of the
///         way, and a juke will not step onto one. An NPC without this ignores every sort of danger, and so does one that
///         leaves a sort out: one that fire cannot hurt does not list fire.
/// </summary>
/// <remarks>
///     Dangers come and go far too often to be part of the navmesh, which every NPC shares and which is costly to rebuild.
///         See <c>PathfindingSystem.Klovn.Danger.cs</c>.
///
///     A danger can still start under it. Getting out of one is HTN's, through <c>StandingInDangerPrecondition</c>, so an NPC
///         with this needs that branch at the top of its root.
/// </remarks>
[RegisterComponent]
public sealed partial class NpcAvoidsTileDangerComponent : Component
{
    [DataField]
    public NpcTileDangerKind Dangers = NpcTileDangerKind.None;

    /// <summary>
    ///     How many times what a tile would otherwise cost a dangerous one costs: how much further it will walk to keep off
    ///         one. A wall would leave it stranded behind a fire with nowhere else to go.
    /// </summary>
    [DataField]
    public float Cost = 8f;

    /// <summary>
    ///     The same, for a tile touching a dangerous one. Enough for a path to leave a tile's gap where it costs little: a
    ///         mob at a run carries on a way past a hard turn, and it takes only a graze to catch light.
    /// </summary>
    [DataField]
    public float MarginCost = 2f;
}
