using Robust.Shared.GameStates;

namespace Content.Shared._KS14.MovementIllusion;

/// <summary>
///     Added to things that won't be moved by <see cref="MovementIllusionSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MovementIllusionFocusComponent : Component;
