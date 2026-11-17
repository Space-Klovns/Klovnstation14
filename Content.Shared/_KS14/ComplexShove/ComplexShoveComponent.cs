using Content.Shared._KS14.KsPopup;
using Content.Shared.Physics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.ComplexShove;

/// <summary>
///     Added to entities whose shoves knock people over, tgstation style:
///         onto tables, into other people and against walls,
///         and onto their side when they are already down.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ComplexShoveComponent : Component
{
    /// <summary>
    ///     How far past the shoved entity to look for what they are shoved into, about one tile.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ProbeDistance = 1f;

    /// <summary>
    ///     Radius of the area at the end of the probe searched for a <see cref="KsShoveObstacleComponent"/>.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ProbeRadius = 0.35f;

    /// <summary>
    ///     Collision *mask* of the raycast that finds walls and other solid things with no obstacle component.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public long CollisionMask = (long)(CollisionGroup.Impassable | CollisionGroup.MidImpassable);

    /// <summary>
    ///     Knockdown when the shoved entity hits a wall, or anything solid with no obstacle component.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan BlockedKnockdownTime = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     Sound played when the shoved entity hits a wall.
    /// </summary>
    [DataField]
    public SoundSpecifier? BlockedSound = new SoundCollectionSpecifier("MetalThud");

    /// <summary>
    ///     Popups when the shoved entity is knocked down against a wall. Each gets <c>shover</c> and <c>target</c>.
    /// </summary>
    [DataField]
    public KsAudiencePopups BlockedPopups = new("ks-shove-wall-user", "ks-shove-wall-target", "ks-shove-wall-others");

    /// <summary>
    ///     Popups when the shoved entity hits a wall but the knockdown doesn't take, such as while weightless.
    /// </summary>
    [DataField]
    public KsAudiencePopups BlockedResistedPopups =
        new("ks-shove-wall-resisted-user", "ks-shove-wall-resisted-target", "ks-shove-wall-resisted-others");

    /// <summary>
    ///     Paralysis when shoving someone who is already down. It can't be renewed until they stand up again.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ChainParalyzeTime = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     Popups when someone already down is kicked onto their side. Each gets <c>shover</c> and <c>target</c>.
    /// </summary>
    [DataField]
    public KsAudiencePopups ParalyzePopups =
        new("ks-shove-paralyze-user", "ks-shove-paralyze-target", "ks-shove-paralyze-others");

    /// <summary>
    ///     Whether shoving someone down, once kicked over, against a wall or anything solid with no obstacle component
    ///         makes them drop what they hold. Obstacles decide for themselves with
    ///         <see cref="KsShoveObstacleComponent.DisarmsAgainst"/>.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool DisarmAgainstWalls = true;

    /// <summary>
    ///     Popups when someone down is made to drop what they hold. Each gets <c>shover</c> and <c>target</c>.
    /// </summary>
    [DataField]
    public KsAudiencePopups DisarmPopups = new("ks-shove-disarm-user", "ks-shove-disarm-target", "ks-shove-disarm-others");

    /// <summary>
    ///     Slowdown status given by a shove that has nothing to knock the target over.
    /// </summary>
    [DataField]
    public EntProtoId StaggerEffect = "KsStatusEffectStaggered";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan StaggerTime = TimeSpan.FromSeconds(3);

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float StaggerSpeedModifier = 0.85f;

    /// <summary>
    ///     Push force applied to targets who are standing.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float StandingPushPower = 800f;

    /// <summary>
    ///     Push force applied to targets who are knocked down or incapacitated.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DownedPushPower = 950f;
}
