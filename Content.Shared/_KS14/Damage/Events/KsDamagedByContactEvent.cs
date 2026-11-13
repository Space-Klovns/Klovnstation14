namespace Content.Shared._KS14.Damage.Events;

/// <summary>
///     Raised on an entity when <see cref="Content.Shared.Damage.Systems.DamageContactsSystem"/> has just damaged it for
///         touching something with <see cref="Content.Shared.Damage.Components.DamageContactsComponent"/>, once per
///         damage tick. Shared: the client raises it too, for whatever it predicts.
/// </summary>
[ByRefEvent]
public record struct KsDamagedByContactEvent;
