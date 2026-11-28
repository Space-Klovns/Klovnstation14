using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

public sealed partial class CultivationMeditateActionEvent : InstantActionEvent;

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

/// <summary>
///     Raised on a cultivator as a meditation cycle's insight is worked out, so anything that helps them learn (fellow
///         sect members nearby, a place in the Martial World Ranking) can add to the multiplier.
/// </summary>
[ByRefEvent]
public record struct CultivationInsightMultiplierEvent(float Multiplier);

/// <summary>
///     Raised on a body cultivator when training earns them tempering; company (sect members and sworn siblings
///         nearby, a place in the Martial World Ranking, the Mandate's favour) can add to the multiplier.
/// </summary>
[ByRefEvent]
public record struct BodyTemperingMultiplierEvent(float Multiplier);

/// <summary>
///     Broadcast when someone reaches the top of a path: Nascent Soul, or the ninth stage of the body.
/// </summary>
[ByRefEvent]
public readonly record struct CultivationPeakReachedEvent(EntityUid Cultivator);
