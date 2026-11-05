namespace Content.Server._KS14.NPC.Dangers;

/// <summary>
///     Has an NPC's paths steer clear of tiles posing the sorts of danger listed here. It goes round them where it can, and
///         crosses them if that is the only way. An NPC without this ignores every sort of danger, and so does one that
///         leaves a sort out: one that fire cannot hurt does not list fire.
/// </summary>
/// <remarks>
///     Dangers come and go far too often to be part of the navmesh, which every NPC shares and which is costly to rebuild.
///         See <c>PathfindingSystem.Klovn.Danger.cs</c>.
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
}
