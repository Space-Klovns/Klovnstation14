using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Ladders;

[Serializable, NetSerializable]
public sealed partial class KsLadderClimbDoAfterEvent : SimpleDoAfterEvent;
