using Content.Shared.Damage.Components;

namespace Content.Shared._KS14.Projectiles;

/// <summary>
///     A projectile that hits entities with <see cref="RequireProjectileTargetComponent"/> even when they would
///         normally let projectiles pass over them, such as someone lying down. For attacks that fill an area, which
///         dropping prone shouldn't dodge.
/// </summary>
[RegisterComponent]
public sealed partial class IgnoreRequireProjectileTargetComponent : Component;
