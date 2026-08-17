using Robust.Shared.GameStates;

namespace Content.Shared._KS14.PhosphorNightVision;

/// <summary>
///     Adds a glow to the entity.
///         Not an always-temporary component.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class PhosphorNightVisionGlowComponent : Component
{
    /// <summary>
    ///     Multiplier of the NV light color added to this when rendering it.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables]
    public float Scale = 0.5f;

    /// <summary>
    ///     If MaxValue, then the glow effect has not started yet.
    /// </summary>
    [AutoNetworkedField]
    public TimeSpan StartTime = TimeSpan.MaxValue;

    [DataField, AutoNetworkedField]
    public TimeSpan Duration = TimeSpan.Zero;
}
