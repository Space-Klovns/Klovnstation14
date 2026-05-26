using Content.Shared.Damage.Components;

namespace Content.Shared._KS14.Damage.Events;

/// <summary>
///     Raised after some stamina damage is done.
/// </summary>
[ByRefEvent]
public record struct KsAfterStaminaDamageEvent(Entity<StaminaComponent> Entity, EntityUid? OriginUid, EntityUid? UsedUid, float Damage);
