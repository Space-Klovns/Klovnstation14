using System.Numerics;
using Robust.Shared.Map;

namespace Content.Client._KS14.LaserSight;

/// <summary>
///     Client-only cache of a laser's last obstacle raycast, so a still beam is not re-raycast every frame.
/// </summary>
[RegisterComponent, Access(typeof(KsLaserSightOverlaySystem))]
public sealed partial class KsLaserSightClientStateComponent : Component
{
    [ViewVariables]
    public MapId MapId;

    [ViewVariables]
    public Vector2 Origin;

    [ViewVariables]
    public Vector2 Direction;

    /// <summary>
    ///     <see cref="Content.Shared._KS14.LaserSight.KsLaserSightComponent.MaxLength"/> the length was resolved against.
    /// </summary>
    [ViewVariables]
    public float MaxLength;

    /// <summary>
    ///     The resolved beam length: the nearest obstacle distance, or the max length when nothing is hit.
    /// </summary>
    [ViewVariables]
    public float Length;

    [ViewVariables]
    public TimeSpan ResolvedAt;

    [ViewVariables]
    public bool Valid;
}
