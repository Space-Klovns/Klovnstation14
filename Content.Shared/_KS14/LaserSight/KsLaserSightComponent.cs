using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.LaserSight;

/// <summary>
///     Draws a laser sight along the entity's world facing.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsLaserSightSystem))]
public sealed partial class KsLaserSightComponent : Component
{
    /// <summary>
    ///     Whether the laser is drawn right now. Written by the server.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>
    ///     If true, the server enables the laser only while an NPC ranged-combat target is in line of sight.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool OnlyWhileTargeting;

    [DataField, AutoNetworkedField]
    public Color Color = Color.Red;

    /// <summary>
    ///     Core beam width in metres.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Width = 0.05f;

    /// <summary>
    ///     Width of the wider, faint glow quad in metres. Zero disables it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float GlowWidth = 0.2f;

    [DataField, AutoNetworkedField]
    public float GlowAlpha = 0.25f;

    /// <summary>
    ///     Emissive bloom intensity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Intensity = 2.2f;

    [DataField, AutoNetworkedField]
    public float MaxLength = 12f;

    /// <summary>
    ///     Sets the fade length, <c>(1 - FadeStart) * MaxLength</c> metres. The beam is opaque up to that distance
    ///         before its end and falls linearly to transparent exactly at its end: at <see cref="MaxLength"/> for a
    ///         free beam, or at the hit point for one cut short by an obstacle (same fade length, starting earlier,
    ///         at the origin if the cut is closer than the fade length). 1 disables the fade.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float FadeStart = 0.5f;

    /// <summary>
    ///     Origin offset in the entity's local frame.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 Offset = Vector2.Zero;

    /// <summary>
    ///     Added to the entity's facing, for sprites whose barrel is not "forward".
    /// </summary>
    [DataField, AutoNetworkedField]
    public Angle AngleOffset = Angle.Zero;

    /// <summary>
    ///     Whether the client shortens the beam at the first opaque obstacle.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool StopAtObstacles = true;

    /// <summary>
    ///     Whether the laser blinks while the entity's lock-on is still locking.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool BlinkWhileLocking = true;

    [DataField, AutoNetworkedField]
    public float BlinkFrequency = 6f;
}
