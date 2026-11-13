using Robust.Shared.Network;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     How much damage each defender has dealt to a unit, so that a kill is paid out by share rather than to
///         whoever landed the last shot.
/// </summary>
[RegisterComponent, Access(typeof(KsFieldCommanderRuleSystem))]
public sealed partial class KsFieldCommanderDamageLedgerComponent : Component
{
    [ViewVariables]
    public Dictionary<NetUserId, float> Damage = new();
}
