using Robust.Shared.Network;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Points for keeping the other defenders alive: for bringing back the dead, and for healing the wounded. Each is
///         paid less the more of the harm was the medic's own doing, and healing oneself pays nothing.
/// </summary>
public sealed partial class KsFieldCommanderRuleComponent
{
    /// <summary>
    ///     Points for reviving a dead defender, times one less the reviver's share of the damage that killed them.
    /// </summary>
    [DataField]
    public int RevivePoints = 50;

    /// <summary>
    ///     How long after a paid revive reviving the same defender pays again.
    /// </summary>
    [DataField]
    public TimeSpan ReviveCooldown = TimeSpan.FromMinutes(3);

    /// <summary>
    ///     Points for each point of damage healed on another living defender, times one less the healer's share of the
    ///         damage they have taken.
    /// </summary>
    [DataField]
    public float HealPointsPerDamage = 0.5f;

    /// <summary>
    ///     What each healer has earned towards their next whole point, as one heal can be worth less than one.
    /// </summary>
    [ViewVariables]
    public Dictionary<NetUserId, float> HealPointsRemainder = new();
}
