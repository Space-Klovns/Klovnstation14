using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;

namespace Content.Client._KS14.Shuttles.UI;

/// <summary>
///     Client-side threat grouping shared by the ESM screen's warning box and the
///         RADAR status readout. One implementation (and one RWR-fit gate applied by
///         both callers) so both tabs always show the same posture; the server sends
///         contacts, never channels or postures.
/// </summary>
public static class KsThreatEval
{
    /// <summary>
    ///     The broad threat class the contact's FRESHEST emitter-class source heard
    ///         (Jammer -> jammer, ELINT/RWR -> search radar). Freshest wins so a grid
    ///         that stopped jamming and lit its radar reclassifies with the newer
    ///         knowledge.
    /// </summary>
    public static KsEmitterThreatClass? ThreatClass(KsSensorContactState contact)
    {
        KsEmitterThreatClass? cls = null;
        var seen = TimeSpan.MinValue;

        foreach (var source in contact.Sources)
        {
            if (source.LastSeen <= seen)
                continue;

            switch (source.Type)
            {
                case KsSensorType.Jammer:
                    cls = KsEmitterThreatClass.Jammer;
                    seen = source.LastSeen;
                    break;
                case KsSensorType.Elint:
                case KsSensorType.Rwr:
                    cls = KsEmitterThreatClass.Radar;
                    seen = source.LastSeen;
                    break;
            }
        }

        return cls;
    }

    /// <summary>
    ///     The first channel in <paramref name="channels"/> (callers keep them sorted
    ///         by descending priority) matching the contact's class and band, if any.
    /// </summary>
    public static KsThreatChannelPrototype? MatchChannel(KsSensorContactState contact, List<KsThreatChannelPrototype> channels)
    {
        if (ThreatClass(contact) is not { } cls)
            return null;

        foreach (var channel in channels)
        {
            if (channel.Class != cls)
                continue;

            if (channel.Bands.Count > 0 && (contact.Band is not { } band || !channel.Bands.Contains(band)))
                continue;

            return channel;
        }

        return null;
    }

    /// <summary>
    ///     The posture for the current threat picture: the first posture in
    ///         <paramref name="postures"/> (callers keep them sorted by descending
    ///         Order) whose thresholds the counts meet.
    /// </summary>
    public static KsPosturePrototype? PickPosture(List<KsPosturePrototype> postures, int threatCount, int? litPriority)
    {
        foreach (var candidate in postures)
        {
            if (threatCount >= candidate.MinThreats
                || candidate.MinChannelPriority is { } minPriority && litPriority >= minPriority)
            {
                return candidate;
            }
        }

        return null;
    }
}
