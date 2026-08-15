using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Sensors.Prototypes;

/// <summary>
///     The broad kind of threat an emission represents, matched by threat channels.
///         Derived client-side from a contact's emitter-class sources: an ELINT/RWR
///         return is a heard search radar, a Jammer classification a jammer.
/// </summary>
public enum KsEmitterThreatClass : byte
{
    Radar = 0,
    Jammer = 1,
}

/// <summary>
///     One RWR threat channel: a named bucket the warning receiver groups heard
///         emitters into (SEARCH, JAM). A contact belongs to the highest-priority
///         channel whose <see cref="Class"/> it carries and whose <see cref="Bands"/>
///         list (empty = any) contains its heard band. Purely client-side grouping:
///         the server sends contacts, never channels.
/// </summary>
[Prototype]
public sealed partial class KsThreatChannelPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The channel's readout label ("SEARCH").</summary>
    [DataField(required: true)]
    public LocId Label;

    /// <summary>
    ///     Urgency rank: among channels matching one contact the highest priority
    ///         claims it, the threat stack sorts descending by it, and postures may
    ///         escalate off it (<see cref="KsPosturePrototype.MinChannelPriority"/>).
    /// </summary>
    [DataField]
    public int Priority;

    /// <summary>The emitter class this channel matches.</summary>
    [DataField(required: true)]
    public KsEmitterThreatClass Class;

    /// <summary>
    ///     Bands this channel matches; empty matches any band, including a
    ///         contact whose band is still unclassified.
    /// </summary>
    [DataField]
    public List<ProtoId<KsEmitterBandPrototype>> Bands = new();

    /// <summary>The channel's tint on the RWR plot and threat stack.</summary>
    [DataField]
    public Color Color = Color.FromHex("#FFC000");

    /// <summary>
    ///     Threat-strobe blink rate in Hz. The blink's curve is the HUD prototype's
    ///         ThreatStrobe anim; this only sets how fast it runs, so a higher-priority
    ///         channel can blink faster.
    /// </summary>
    [DataField]
    public float BlinkHz = 1f;
}
