using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Damage;

/// <summary>
///     Penetration has to survive every modifier set a hit goes through. A target's own set is applied before its
///         armour, and each armour piece after the last, so a set that drops it disarms the round for the rest.
/// </summary>
[TestFixture]
[TestOf(typeof(DamageSpecifier))]
public sealed class KsArmorPenetrationTest
{
    private const string Piercing = "Piercing";

    private static DamageSpecifier PenetratingRound()
    {
        var damage = new DamageSpecifier();
        damage.DamageDict[Piercing] = FixedPoint2.New(100);
        damage.PercentilePenetration = new() { [Piercing] = 0.5f };
        damage.FlatPenetration = new() { [Piercing] = 2f };
        damage.DisableCrossInteraction = true;
        return damage;
    }

    private static readonly DamageModifierSet HalvingArmor = new() { Coefficients = { [Piercing] = 0.5f } };

    /// <summary>
    ///     The Vox set: it has no say over piercing damage at all.
    /// </summary>
    private static readonly DamageModifierSet SpeciesSet = new() { Coefficients = { ["Poison"] = 1f } };

    [Test]
    public void SpeciesSetDoesNotStripPenetrationFromArmor()
    {
        var bare = DamageSpecifier.ApplyModifierSet(PenetratingRound(), HalvingArmor);
        var behindSpeciesSet = DamageSpecifier.ApplyModifierSet(DamageSpecifier.ApplyModifierSet(PenetratingRound(), SpeciesSet), HalvingArmor);

        // Half the armour's 50% is punched through: 75 of 100, not 50.
        Assert.That(bare.DamageDict[Piercing].Float(), Is.EqualTo(75f).Within(0.01f));
        Assert.That(behindSpeciesSet.DamageDict[Piercing].Float(), Is.EqualTo(75f).Within(0.01f),
            "Armour on a target with its own modifier set must be penetrated as much as on one without.");
    }

    [Test]
    public void SecondArmorPieceIsPenetratedToo()
    {
        var twice = DamageSpecifier.ApplyModifierSets(PenetratingRound(), [HalvingArmor, HalvingArmor]);

        Assert.That(twice.DamageDict[Piercing].Float(), Is.EqualTo(56.25f).Within(0.01f),
            "Each armour piece must face the round's penetration, not only the first.");
    }

    [Test]
    public void DerivedSpecifiersKeepPenetration()
    {
        var round = PenetratingRound();

        Assert.Multiple(() =>
        {
            AssertKeepsPenetration(DamageSpecifier.ApplyModifierSet(round, SpeciesSet), nameof(DamageSpecifier.ApplyModifierSet));
            AssertKeepsPenetration(new DamageSpecifier(round), "copy constructor");
            AssertKeepsPenetration(DamageSpecifier.GetPositive(round), nameof(DamageSpecifier.GetPositive));
            AssertKeepsPenetration(DamageSpecifier.GetNegative(round), nameof(DamageSpecifier.GetNegative));
            AssertKeepsPenetration(round / FixedPoint2.New(2), "division by a FixedPoint2");
            AssertKeepsPenetration(round / 2f, "division by a float");
            AssertKeepsPenetration(round * 2f, "multiplication");
        });
    }

    private static void AssertKeepsPenetration(DamageSpecifier derived, string how)
    {
        Assert.That(derived.PercentilePenetration, Is.Not.Null.And.ContainKey((Robust.Shared.Prototypes.ProtoId<DamageTypePrototype>)Piercing), $"{how} dropped the percentile penetration.");
        Assert.That(derived.FlatPenetration, Is.Not.Null.And.ContainKey((Robust.Shared.Prototypes.ProtoId<DamageTypePrototype>)Piercing), $"{how} dropped the flat penetration.");
        Assert.That(derived.DisableCrossInteraction, Is.True, $"{how} dropped DisableCrossInteraction.");
    }
}
