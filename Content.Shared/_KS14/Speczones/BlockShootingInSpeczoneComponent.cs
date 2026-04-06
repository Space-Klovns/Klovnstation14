using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Speczones;

/// <summary>
///     Added to guns to block them from being fired when in speczones.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BlockShootingInSpeczoneComponent : Component;
