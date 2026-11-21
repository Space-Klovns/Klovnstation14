using Content.Shared.Damage;

namespace Content.Shared._KS14.Damage.Events;

/// <summary>
///     Raised on an entity, on a client, when that client's kept copy of its own shot hit it: the damage the hit would
///         deal, which only the server's hit deals. For what a hit shows at once (its blood), so the shooter does not
///         wait a round trip for it. Handlers play effects, and leave the entity's networked state alone: nothing
///         about a kept copy's hit is re-predicted, so a change made here would be put back by the next state.
/// </summary>
/// <param name="Damage">What the hit would deal, resistances applied.</param>
/// <param name="Origin">Who shot it, if anyone.</param>
[ByRefEvent]
public readonly record struct KsPreviewedDamageEvent(DamageSpecifier Damage, EntityUid? Origin);
