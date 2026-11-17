using Content.Shared.Alert;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Carrying;

/// <summary>
///     Raised on someone carried as they stop being carried, on every side, including when the server's state says so.
/// </summary>
[ByRefEvent]
public readonly record struct KsCarryEndedEvent(EntityUid Carrier);

[Serializable, NetSerializable]
public sealed partial class KsFiremanCarryDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class KsEscapeCarryDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
///     Clicking the carrying alert puts whoever you carry down.
/// </summary>
public sealed partial class KsStopCarryingAlertEvent : BaseAlertEvent;

/// <summary>
///     Clicking the carried alert tries to wriggle free.
/// </summary>
public sealed partial class KsEscapeCarryAlertEvent : BaseAlertEvent;
