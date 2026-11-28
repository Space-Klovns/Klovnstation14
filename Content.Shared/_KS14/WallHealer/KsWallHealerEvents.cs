using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.WallHealer;

/// <summary>
///     Someone is putting a patient's hand, maybe their own, into a wall healer. The patient is the do-after's target.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class KsWallHealerInsertHandDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
///     Runs, repeating, for as long as a patient's hand is in a wall healer. Each time it completes the patient is
///         treated; when it is cancelled their hand comes out.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class KsWallHealerTreatmentDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
///     Raised on a wall healer when how full its pools are has changed, predicted or not, so the client redraws them.
/// </summary>
[ByRefEvent]
public readonly record struct KsWallHealerPoolsChangedEvent;
