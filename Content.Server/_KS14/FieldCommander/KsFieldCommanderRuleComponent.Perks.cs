using Content.Shared._KS14.Perks;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Perks: each defender picks one, once per round, and gets it again on every body the ticker gives them.
/// </summary>
public sealed partial class KsFieldCommanderRuleComponent
{
    #region Perks

    /// <summary>
    ///     The perks a defender picks from. Empty, and nobody is asked.
    /// </summary>
    [DataField]
    public List<ProtoId<KsPerkPrototype>> PerkOptions = new();

    /// <summary>
    ///     Each defender's pick, kept for the round so later bodies get it without asking again.
    /// </summary>
    [ViewVariables]
    public Dictionary<NetUserId, ProtoId<KsPerkPrototype>> ChosenPerks = new();

    #endregion
}
