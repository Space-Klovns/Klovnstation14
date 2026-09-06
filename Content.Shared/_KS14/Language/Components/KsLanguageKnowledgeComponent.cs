using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Language.Components;

/// <summary>
///     Intrinsically known languages, authored on species mob prototypes. Absent component means
///     the entity knows only the station default (vanilla behavior).
/// </summary>
[RegisterComponent]
public sealed partial class KsLanguageKnowledgeComponent : Component
{
    [DataField]
    public List<ProtoId<KsLanguagePrototype>> Speaks = new();

    [DataField]
    public List<ProtoId<KsLanguagePrototype>> Understands = new();
}
