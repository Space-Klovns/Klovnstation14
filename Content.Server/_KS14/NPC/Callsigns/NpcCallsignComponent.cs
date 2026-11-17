using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Callsigns;

/// <summary>
///     Gives this NPC a callsign from <see cref="Set"/> when it spawns, one no other living NPC on the map has, shown after
///         its name or as it (<see cref="ReplacesName"/>), and on its ID card (<see cref="NamesIdCard"/>). A squadmate that sees it go down says so by callsign, in a voice that calls out men down. See
///         <see cref="NpcCallsignSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcCallsignSystem))]
public sealed partial class NpcCallsignComponent : Component
{
    [DataField(required: true)]
    public ProtoId<NpcCallsignSetPrototype> Set;

    /// <summary>
    ///     Whether the callsign is its whole name, rather than going after the one it spawned with.
    /// </summary>
    [DataField]
    public bool ReplacesName;

    /// <summary>
    ///     Whether the callsign also goes on the ID card it spawns with: behind a mask, it is the card's name that shows.
    /// </summary>
    [DataField]
    public bool NamesIdCard;

    /// <summary>
    ///     Which of the set's callsigns it has: null for none, with every one already taken.
    /// </summary>
    [ViewVariables]
    public int? Index;
}
