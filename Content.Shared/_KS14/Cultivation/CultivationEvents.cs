using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

public sealed partial class CultivationMeditateActionEvent : InstantActionEvent;

public sealed partial class CultivationBreakthroughActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class CultivationMeditateDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CultivationBreakthroughDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CultivationAscensionDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CultivationReadManualDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
///     Raised on a cultivator after they break through into a new realm.
/// </summary>
[ByRefEvent]
public readonly record struct CultivationRealmChangedEvent(CultivationRealm OldRealm, CultivationRealm NewRealm);

/// <summary>
///     Raised on a cultivator to ask whether another entity counts as a friend, for techniques that help allies or
///         spare them. Anything that binds people together (sworn brotherhood, sects) answers it.
/// </summary>
[ByRefEvent]
public record struct CultivationAllyCheckEvent(EntityUid Other, bool Ally = false);

/// <summary>
///     Raised on a cultivator when their breakthrough readiness is assessed, so anything that steadies them can add to it.
/// </summary>
[ByRefEvent]
public record struct CultivationBreakthroughReadinessEvent(int Readiness);
