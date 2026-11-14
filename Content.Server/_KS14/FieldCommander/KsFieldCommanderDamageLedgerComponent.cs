using Robust.Shared.Network;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     How much damage each defender has dealt to a unit or to another defender, so that a kill, or a teamkill, is
///         paid out by share rather than to whoever landed the last shot. Kept per player, settled as each hit lands:
///         what dealt it (a turret, a bullet) may be gone by the time of the death.
/// </summary>
[RegisterComponent, Access(typeof(KsFieldCommanderRuleSystem))]
public sealed partial class KsFieldCommanderDamageLedgerComponent : Component
{
    [ViewVariables]
    public Dictionary<NetUserId, float> Damage = new();

    /// <summary>
    ///     Damage that no defender is paid for: the enemy's, the world's, the entity's own. Only kept on a defender,
    ///         where a teamkill is weighed against it.
    /// </summary>
    [ViewVariables]
    public float OtherDamage;
}
