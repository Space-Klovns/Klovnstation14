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
    ///     Stains that are on this entity, with their color,
    ///         with the vector's 2 first elements being its X and Y offset,
    ///         and 3rd element being from 0 to 1 specifying its rotation.
    /// </summary>
    [AutoNetworkedField]
    public List<(Vector3, Color)> Stains = new();
}

[Serializable, NetSerializable]
public enum StainOverlayVisuals : byte
{
    Count
}
