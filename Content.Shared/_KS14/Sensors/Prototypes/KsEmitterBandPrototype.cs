using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Sensors.Prototypes;

/// <summary>
///     A frequency band an emitter (radar or jammer) transmits on. Pure identification
///         intel: ELINT reveals it about a located emitter so the crew can classify what
///         kind of set is out there. Nothing is keyed on band (no band-matched jamming,
///         no receiver band coverage), so adding a band changes only the readout.
/// </summary>
[Prototype]
public sealed partial class KsEmitterBandPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The band's readout label ("LOW BAND").</summary>
    [DataField(required: true)]
    public LocId Label;

    /// <summary>Roster sort order among bands; lower sorts first.</summary>
    [DataField]
    public int SortOrder;
}

/// <summary>
///     An emitter's transmission pattern over time. Identification intel only; only
///         <see cref="Continuous"/> exists today, and the enum is on the wire already so
///         later values cost no wire-format change.
/// </summary>
[Serializable, NetSerializable]
public enum KsEmissionPattern : byte
{
    Continuous = 0,
}
