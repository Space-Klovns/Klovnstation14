using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

/// <summary>
///     The ancient master in an ancestral ring speaks into its bearer's head. Nobody else hears.
/// </summary>
public sealed partial class RingElderSpeakActionEvent : InstantActionEvent;

/// <summary>
///     The ancient master pours some of their qi into the bearer.
/// </summary>
public sealed partial class RingElderLendQiActionEvent : InstantActionEvent
{
    [DataField]
    public float Qi = 30f;
}

[Serializable, NetSerializable]
public sealed partial class SpiritHerbPickDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
///     Point at a disciple or sworn sibling to name them heir to your cultivation; point at yourself to name no heir.
/// </summary>
public sealed partial class NameHeirActionEvent : EntityTargetActionEvent;

// The Mandate of Heaven.
public sealed partial class ImperialDecreeActionEvent : InstantActionEvent;

public sealed partial class MandateKneelActionEvent : InstantActionEvent;

public sealed partial class MandateTreasuryActionEvent : InstantActionEvent;

public sealed partial class MandatePardonActionEvent : EntityTargetActionEvent;

public sealed partial class MandateJinyiweiActionEvent : EntityTargetActionEvent;

public sealed partial class MandateFealtyActionEvent : EntityTargetActionEvent;

public sealed partial class MandateEnnobleActionEvent : EntityTargetActionEvent;

public sealed partial class MandatePunishmentActionEvent : EntityTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class JadeSealClaimDoAfterEvent : SimpleDoAfterEvent;
