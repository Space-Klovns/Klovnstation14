using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ComplexShove;

/// <summary>
///     Something a shoved entity can be shoved into, such as a table or another person.
///     The shove stops here and knocks the shoved entity over, see <see cref="ComplexShoveSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsShoveObstacleComponent : Component
{
    /// <summary>
    ///     How long the shoved entity is knocked down for.
    /// </summary>
    [DataField]
    public TimeSpan KnockdownTime = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     How long this obstacle is knocked down for when someone is shoved into it. Null leaves it standing.
    /// </summary>
    [DataField]
    public TimeSpan? CollateralKnockdownTime;

    /// <summary>
    ///     Whether the shoved entity ends up on top of this obstacle, as on a table. Needs a climbable obstacle.
    /// </summary>
    [DataField]
    public bool PlaceOnto;

    /// <summary>
    ///     Whether shoving someone down, once kicked over, into this obstacle makes them drop what they hold, as a wall
    ///         does. Off for tables and people.
    /// </summary>
    [DataField]
    public bool DisarmsAgainst;

    /// <summary>
    ///     When several obstacles are behind the shoved entity, the one with the highest priority is used.
    /// </summary>
    [DataField]
    public int Priority;

    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    ///     Popups for onlookers, the shover and the shoved entity. Each gets <c>shover</c>, <c>target</c> and <c>obstacle</c>.
    /// </summary>
    [DataField]
    public LocId OthersPopup = "ks-shove-into-others";

    [DataField]
    public LocId UserPopup = "ks-shove-into-user";

    [DataField]
    public LocId TargetPopup = "ks-shove-into-target";
}
