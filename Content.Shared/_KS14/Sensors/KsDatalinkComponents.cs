using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Sensors;

public static class KsDatalink
{
    /// <summary>Lowest tunable frequency, inclusive.</summary>
    public const int MinFrequency = 1000;

    /// <summary>Highest tunable frequency, inclusive.</summary>
    public const int MaxFrequency = 3000;
}

/// <summary>
///     Broadcasts everything the mounting grid knows (its full contact pool, live +
///         memory) on a walkie-talkie-style frequency. Any powered
///         <see cref="KsDatalinkReceiverComponent"/> tuned to the same frequency within
///         effective range ingests the broadcast, including enemies who guessed your
///         frequency.
///     Deliberately NOT networked: replicating frequency/power to every client with the
///         machine in PVS would hand cheat clients your settings for free. Learning a
///         frequency takes physical access: examining the machine or reading its UI.
/// </summary>
[RegisterComponent]
public sealed partial class KsDatalinkTransmitterComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Enabled = true;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int Frequency = 1200;

    /// <summary>Hard range cap at 100% power. Ignored entirely when <see cref="UnlimitedRange"/> is set.</summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float MaxRange = 768f;

    /// <summary>
    ///     Mapping-only: heard by every receiver within range regardless of what frequency
    ///         it is tuned to, a public beacon nobody has to guess a channel for. Not
    ///         exposed in the UI (a crafted transmitter can never set it), so a player
    ///         datalink stays a private, tunable, interceptable channel.
    /// </summary>
    [DataField]
    public bool BroadcastAllFrequencies;

    /// <summary>
    ///     Mapping-only: skips the distance falloff and reaches every same-map receiver at
    ///         any range, a sector-wide beacon. <see cref="MaxRange"/> and
    ///         <see cref="PowerFraction"/> no longer bound reach (power still gates
    ///         emitting at all and scales APC load). Not exposed in the UI.
    /// </summary>
    [DataField]
    public bool UnlimitedRange;

    /// <summary>
    ///     Mapping-only: broadcasts with or without power, for unattended public beacons
    ///         that must stay lit on a dead or power-less grid. Still respects
    ///         <see cref="Enabled"/> and a 0% <see cref="PowerFraction"/>. Not exposed in
    ///         the UI.
    /// </summary>
    [DataField]
    public bool IgnorePower;

    /// <summary>
    ///     Power slider setting in [0, 1]: effective broadcast radius is
    ///         <c>PowerFraction * MaxRange</c>. Also scales APC load and, once ELINT can
    ///         hear datalink, how far away the emission can be heard.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float PowerFraction = 1f;

    /// <summary>Not read anywhere yet; carried so YAML can already declare it.</summary>
    [DataField]
    public bool VisibleToElint = true;

    /// <summary>APC load in watts at the 0% slider setting (idle electronics).</summary>
    [DataField]
    public float BasePowerDraw = 200f;

    /// <summary>APC load in watts at the 100% slider setting.</summary>
    [DataField]
    public float MaxPowerDraw = 5000f;

    /// <summary>
    ///     Contacts whose detection travelled this many datalink hops are not rebroadcast,
    ///         loop/echo insurance for relay chains.
    /// </summary>
    [DataField]
    public int HopLimit = 4;

    /// <summary>
    ///     The intel this transmitter folds into its self-report, so a grid listening with
    ///         only a receiver learns the transmitting ship's stats (size, mass, top speed)
    ///         exactly as one of your own sensors would read them off a contact, not just
    ///         its position and outline. Evaluated server-side, mirroring
    ///         <see cref="KsSensorComponent.Intel"/>; clear it in YAML for a transmitter
    ///         that should reveal nothing about itself.
    /// </summary>
    [DataField]
    public List<ProtoId<KsSensorIntelPrototype>> Intel = new() { "KsIntelSize", "KsIntelMass", "KsIntelTopSpeed" };

    /// <summary>
    ///     When true the transmitter forwards what it knows about OTHER grids (its whole
    ///         contact pool). False means it relays nothing it detected, only (if
    ///         <see cref="AnnounceSelf"/>) its own self-report: a pure position beacon.
    /// </summary>
    [DataField]
    public bool RelayContacts = true;

    /// <summary>
    ///     When true the transmitter announces its own grid (position, outline, name,
    ///         intel) to the network. False means a pure relay/repeater that forwards
    ///         allies' tracks but never reveals its own position.
    /// </summary>
    [DataField]
    public bool AnnounceSelf = true;

    /// <summary>
    ///     When true the self-report carries the grid's name; false means an anonymous
    ///         outline at the transmitter's position. Ignored when
    ///         <see cref="AnnounceSelf"/> is false.
    /// </summary>
    [DataField]
    public bool RevealName = true;

    /// <summary>
    ///     How the self-report renders on allied consoles (Outline silhouette vs Blip dot).
    ///         Ignored when <see cref="AnnounceSelf"/> is false.
    /// </summary>
    [DataField]
    public KsContactRenderMode SelfRenderMode = KsContactRenderMode.Outline;
}

/// <summary>
///     Listens on a frequency and feeds every broadcast it can hear into the mounting
///         grid's contact pool, effectively a supersensor whose "detections" are another
///         network's knowledge. Passive: emits nothing. Not networked, same reasoning as
///         the transmitter.
/// </summary>
[RegisterComponent]
public sealed partial class KsDatalinkReceiverComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Enabled = true;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int Frequency = 1200;
}
