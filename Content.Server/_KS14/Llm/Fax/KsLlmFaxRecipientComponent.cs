using Content.Server._KS14.Llm.Prototypes;
using Content.Shared.Paper;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Llm.Fax;

/// <summary>
///     Faxes received by this machine are answered by an LLM persona, which replies by fax to the sender.
/// </summary>
[RegisterComponent, Access(typeof(KsLlmFaxSystem))]
public sealed partial class KsLlmFaxRecipientComponent : Component
{
    [DataField]
    public ProtoId<KsLlmPersonaPrototype> Persona = "KsLlmCentralCommand";

    [DataField]
    public LocId ReplyTitle = "ks-llm-fax-reply-title";

    /// <summary>
    ///     Sprite state of the stamps on replies, from <c>bureaucracy.rsi</c>.
    /// </summary>
    [DataField]
    public string ReplyStampState = "paper_stamp-centcom";

    [DataField]
    public List<StampDisplayInfo> ReplyStampedBy = new()
    {
        new StampDisplayInfo { StampedName = "stamp-component-stamped-name-centcom", StampedColor = Color.FromHex("#BB3232") },
    };
}
