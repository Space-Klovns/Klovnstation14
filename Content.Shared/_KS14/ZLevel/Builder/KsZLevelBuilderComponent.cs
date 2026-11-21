using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.ZLevel.Builder;

/// <summary>
///     Used in hand, puts <see cref="LowerLadder"/> on the user's tile and <see cref="UpperLadder"/> directly above it on
///         the z-level above, linked. Creates that z-level if there is none, at most once per <see cref="LevelDelayId"/>
///         of the item's <c>UseDelay</c>.
/// </summary>
[RegisterComponent]
public sealed partial class KsZLevelBuilderComponent : Component
{
    [DataField]
    public EntProtoId LowerLadder = "KsLadderWall";

    [DataField]
    public EntProtoId UpperLadder = "KsLadderFloor";

    /// <summary>
    ///     Floor put under <see cref="UpperLadder"/> when there is no grid to stand it on.
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition> Tile = "Plating";

    /// <summary>
    ///     The <c>UseDelay</c> delay started when a z-level is created, and checked before one is. Not the default
    ///         delay: that one would block every use, ladder-building on an existing level included.
    /// </summary>
    [DataField]
    public string LevelDelayId = "ksZLevelBuilderLevel";

    /// <summary>
    ///     How far from the spot a ladder already counts as being there.
    /// </summary>
    [DataField]
    public float LadderClearance = 0.45f;
}
