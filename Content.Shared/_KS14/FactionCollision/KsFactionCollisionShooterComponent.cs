using Robust.Shared.GameStates;

namespace Content.Shared._KS14.FactionCollision;

/// <summary>
///     For entities that, when shooting, will have <see cref="KsFactionCollisionComponent"/>
///         with their current factions added to the shot projectiles.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsFactionCollisionShooterComponent : Component;
