namespace Content.Shared._KS14.Sensors;

/// <summary>
///     Marks an entity (typically a wall) as contributing to its grid's thermal
///         signature, the quantity IRST sensors detect. A grid's signature is the
///         sum, over every carrier, of <see cref="Signature"/> times how many of
///         the eight surrounding tiles (faces + corners) are open to space, so a
///         corner or protruding wall radiates more than a flush one and a fully
///         boxed-in wall contributes nothing. A heat-managed wall just carries a
///         lower <see cref="Signature"/>, so "running cold" needs no special-case code.
/// </summary>
[RegisterComponent]
public sealed partial class KsThermalSourceComponent : Component
{
    /// <summary>Heat radiated per exposed side/corner, in IRST signature units.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Signature = 1f;
}
