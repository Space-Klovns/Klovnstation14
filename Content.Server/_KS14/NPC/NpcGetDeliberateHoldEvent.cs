namespace Content.Server._KS14.NPC;

/// <summary>
///     Raised on an NPC to ask whether it is standing where it is on purpose: lying still playing dead, holding a spot a
///         squad's hunt told it to hold. Something that judges NPCs by whether they get anywhere (the field commander's
///         stuck units) asks this before calling one stuck. Pure: handlers only set <see cref="Holding"/>.
/// </summary>
[ByRefEvent]
public record struct NpcGetDeliberateHoldEvent(bool Holding = false);
