using Robust.Shared.Network;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     On a defender who has died: who brought them down, kept from their damage ledger as it is wiped at the death,
///         so that whoever revives them is paid less for undoing their own work. Kept through the revive, for the
///         cooldown on paying for it again.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsFieldCommanderRuleSystem))]
public sealed partial class KsFieldCommanderDeathRecordComponent : Component
{
    /// <summary>
    ///     The damage each defender had dealt them by the death.
    /// </summary>
    [ViewVariables]
    public Dictionary<NetUserId, float> Damage = new();

    /// <summary>
    ///     The rest of the damage they had taken by the death: the enemy's, the world's, their own.
    /// </summary>
    [ViewVariables]
    public float OtherDamage;

    /// <summary>
    ///     The part of <see cref="OtherDamage"/> the commander's side dealt
    ///         (<see cref="KsFieldCommanderDamageLedgerComponent.AttackerDamage"/>).
    /// </summary>
    [ViewVariables]
    public float AttackerDamage;

    /// <summary>
    ///     When a revive of them was last paid for, if ever.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan? LastRevivePaidTime;
}
