using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Language.Components;

/// <summary>
///     Grants languages to whoever benefits from this entity: on a mob intrinsic (job specials),
///     on a held/worn item a translator device (inactive while its ItemToggle is off), on a
///     subdermal implant while implanted. The kernel rescans live sources on every recompute, so
///     overlapping grants can't corrupt each other.
/// </summary>
[RegisterComponent]
public sealed partial class KsLanguageGrantComponent : Component
{
    [DataField]
    public List<ProtoId<KsLanguagePrototype>> Speaks = new();

    [DataField]
    public List<ProtoId<KsLanguagePrototype>> Understands = new();

    /// <summary>
    ///     Languages the beneficiary must intrinsically know; checked live at every recompute.
    /// </summary>
    [DataField]
    public List<ProtoId<KsLanguagePrototype>> Requires = new();

    [DataField]
    public bool RequiresAll;

    [DataField]
    public bool Enabled = true;
}
