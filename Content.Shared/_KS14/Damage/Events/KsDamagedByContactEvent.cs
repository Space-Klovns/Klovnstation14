namespace Content.Shared._KS14.Damage.Events;

/// <summary>
///     Raised on an entity when <see cref="Content.Shared.Damage.Systems.DamageContactsSystem"/> has just damaged it for
///         touching something with <see cref="Content.Shared.Damage.Components.DamageContactsComponent"/>: once per
///         damage tick for each source that hurt it, which of several sources of one exclusive group it touches is one.
///         Shared: the client raises it too, for whatever it predicts.
/// </summary>
/// <param name="SourceUid">The source whose damage it was.</param>
[ByRefEvent]
public readonly record struct KsDamagedByContactEvent(EntityUid SourceUid);
