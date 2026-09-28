using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Damage;

public sealed partial class DamageSpecifier
{
    /// <summary>
    ///     KS14 addition
    ///     This is just a way for you to specify that a projectile/whatever gets through the percentile reduction given by armor.
    ///     It works via a percentile system - 0 is no AP, 1 is full AP.
    ///     If you go over 1 or under -1 the system will make stupid mistakes, please do not do it
    ///     I removed the clamp because I trust the sanity of my contribs and I can squeeze some minimal perf out by removing it
    ///     Why -1? Because you can set it to -1 to make armor twice as effective against it - could be fun for hollow points!
    /// </summary>

    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, float>? PercentilePenetration;

    /// <summary>
    ///     KS14
    ///     This is just a way for you to specify that a projectile/whatever ignores some flat reduction given by armor.
    ///     It works by subtracting these reductions from the armor's reductions, of course that can't go below zero.
    ///     If you set this to a negative number it actually adds to the flat resistances - could be useful for something.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, float>? FlatPenetration;

    /// <summary>
    ///     KS14
    ///     Right now percentile damage penetration applies to flat damage resistances
    ///     If an armor has 10 flat slash resistance but you have 40% slash ap then it decreases that resist to 6 slash
    ///     This is so its more intuitive (having percentile ap ruins everyone). You can however disable it
    /// </summary>
    [DataField]
    public bool DisableCrossInteraction = false;
}
