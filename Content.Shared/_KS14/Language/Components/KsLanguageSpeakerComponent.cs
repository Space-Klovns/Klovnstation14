using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Language.Components;

/// <summary>
///     Runtime cache of effective languages (knowledge plus active grants); never author in
///     YAML. Replicates only to the owning player; other clients must not learn what an entity
///     understands.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class KsLanguageSpeakerComponent : Component
{
    [AutoNetworkedField]
    public ProtoId<KsLanguagePrototype>? CurrentLanguage;

    [AutoNetworkedField]
    public List<ProtoId<KsLanguagePrototype>> Spoken = new();

    [AutoNetworkedField]
    public List<ProtoId<KsLanguagePrototype>> Understood = new();

    public override bool SendOnlyToOwner => true;
}
