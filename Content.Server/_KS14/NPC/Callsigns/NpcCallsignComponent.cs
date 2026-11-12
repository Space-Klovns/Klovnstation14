using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Callsigns;

/// <summary>
///     Gives this NPC a callsign from <see cref="Set"/> when it spawns, one no other living NPC on the map has, shown after
///         its name. A squadmate that sees it go down says so by callsign, in a voice that calls out men down. See
///         <see cref="NpcCallsignSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcCallsignSystem))]
public sealed partial class NpcCallsignComponent : Component
{
    [DataField(required: true)]
    public ProtoId<NpcCallsignSetPrototype> Set;

    /// <summary>
    ///     Which of the set's callsigns it has: null for none, with every one already taken.
    /// </summary>
    [ViewVariables]
    public int? Index;
}
