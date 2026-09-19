using Content.Client.Damage;

namespace Content.Client._KS14.Damage;

/// <summary>
///     Gets a set of layer map keys to remove.
/// </summary>
/// <param name="RemovedLayerMapKeys">Should be added by subscribers that want to remove something.</param>
[ByRefEvent]
public record struct KsGetDamageVisualsEvent(DamageVisualsComponent DamageVisualsComponent, List<Enum>? RemovedLayerMapKeys);
