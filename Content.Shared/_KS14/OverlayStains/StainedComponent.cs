using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.StainOverlays;

/// <summary>
///     Component to visualise blood-stains on things.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StainedComponent : Component
{
    /// <summary>
    ///     Stains that are on this entity, with their color
    ///         and offset from the center of the entity.
    /// </summary>
    [AutoNetworkedField]
    public List<(Vector2, Color)> Stains = new();
}

[Serializable, NetSerializable]
public enum StainOverlayVisuals : byte
{
    Count
}
