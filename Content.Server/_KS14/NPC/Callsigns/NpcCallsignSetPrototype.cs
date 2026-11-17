using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Callsigns;

/// <summary>
///     The callsigns a kind of NPC goes by, in the order they are handed out, and what a squadmate says when one goes down.
///         See <see cref="NpcCallsignComponent"/>.
/// </summary>
[Prototype]
public sealed partial class NpcCallsignSetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<NpcCallsign> Callsigns = new();
}

/// <summary>
///     One callsign: what it is, and the line a squadmate says when the one going by it goes down.
/// </summary>
[DataDefinition]
public sealed partial class NpcCallsign
{
    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId DownLine;
}
