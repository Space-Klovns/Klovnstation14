using Content.Shared.Dataset;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Voices;

/// <summary>
///     How one kind of NPC says things differently: for each line set its HTN speaks from, the set it uses instead. A
///         mob opts in with <see cref="NpcVoiceSetComponent"/>; a line set this does not mention is spoken as it is. So
///         one HTN can serve several factions that each sound like themselves.
/// </summary>
/// <example>
///     <code>
///     - type: npcVoiceSet
///       id: KsOperativeNt
///       replacements:
///         KsOperativeContact: KsOperativeNtContact
///     </code>
/// </example>
[Prototype]
public sealed partial class NpcVoiceSetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     The line set to speak from instead, by the line set the HTN asks for.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<LocalizedDatasetPrototype>, ProtoId<LocalizedDatasetPrototype>> Replacements = new();

    /// <summary>
    ///     Line sets this voice never says: the task speaking one succeeds without a word. For lines that are wrong for
    ///         a faction altogether, rather than just worded differently.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<LocalizedDatasetPrototype>> Silenced = new();
}
