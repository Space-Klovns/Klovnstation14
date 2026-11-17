using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Bane;

/// <summary>
///     Deals extra damage to whitelisted targets, both in melee and when thrown. tgstation's
///         <c>/datum/component/bane</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsBaneComponent : Component
{
    [DataField(required: true)]
    public EntityWhitelist Whitelist = new();

    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    [DataField]
    public LocId? ExamineText;
}
