using System;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenCoreSelectorTests
{
    [Test]
    public void OrderReplaysAndIgnoresInputEnumerationOrder()
    {
        var cores = new[] { Core("A", 1f), Core("B", 3f), Core("C", 2f) };
        var ordered = KsProcgenCoreSelector.Order(cores, 17, "Room", "Pack", 0);
        Assert.That(KsProcgenCoreSelector.Order(cores.Reverse().ToArray(), 17, "Room", "Pack", 0),
            Is.EqualTo(ordered));
        Assert.That(ordered, Is.EquivalentTo(cores));
    }

    [Test]
    public void DisabledCandidatesDoNotPerturbActiveChoices()
    {
        var cores = new[] { Core("A", 1f), Core("B", 2f) };
        Assert.That(KsProcgenCoreSelector.Order([..cores, Core("Disabled", 0f)], 17, "Room", "Pack", 0),
            Is.EqualTo(KsProcgenCoreSelector.Order(cores, 17, "Room", "Pack", 0)));
        Assert.That(KsProcgenCoreSelector.Order([Core("Disabled", 0f)], 17, "Room", "Pack", 0), Is.Empty);
    }

    [Test]
    public void WeightsAffectFirstChoicesAcrossSeeds()
    {
        var cores = new[] { Core("Heavy", 9f), Core("Light", 1f) };
        var heavyChoices = Enumerable.Range(0, 1024).Count(seed =>
            KsProcgenCoreSelector.Order(cores, seed, "Room", "Pack", 0)[0].Id == "Heavy");
        Assert.That(heavyChoices, Is.InRange(850, 970));
    }

    [Test]
    public void DrawsHaveIndependentDeterministicChoices()
    {
        var cores = new[] { Core("A", 1f), Core("B", 1f), Core("C", 1f) };
        var choices = Enumerable.Range(0, 24).Select(draw =>
            KsProcgenCoreSelector.Order(cores, 17, "Room", "Pack", draw)[0].Id).ToArray();
        Assert.That(choices.Distinct().Count(), Is.EqualTo(3));
        Assert.That(Enumerable.Range(0, 24).Select(draw =>
            KsProcgenCoreSelector.Order(cores, 17, "Room", "Pack", draw)[0].Id), Is.EqualTo(choices));
    }

    [Test]
    public void ExtremePositiveWeightsRemainUsable()
    {
        Assert.That(KsProcgenCoreSelector.Order([Core("Tiny", float.Epsilon), Core("Huge", float.MaxValue)],
            17, "Room", "Pack", 0).Select(core => core.Id), Is.EqualTo(new[] { "Huge", "Tiny" }));
    }

    [Test]
    public void InvalidWeightsDuplicateIdsAndUnboundedPoolsAreRejected()
    {
        foreach (var weight in new[] { -1f, float.NaN, float.PositiveInfinity })
            Assert.Throws<ArgumentException>(() => KsProcgenCoreSelector.Order([Core("Bad", weight)],
                17, "Room", "Pack", 0));
        Assert.Throws<ArgumentException>(() => KsProcgenCoreSelector.Order([Core("A", 1f), Core("A", 2f)],
            17, "Room", "Pack", 0));
        Assert.Throws<ArgumentException>(() => KsProcgenCoreSelector.Order(
            Enumerable.Range(0, 65).Select(index => Core(index.ToString(), 1f)).ToArray(), 17, "Room", "Pack", 0));
    }

    private static KsProcgenResolvedEntityCore Core(string id, float weight) => new(id, weight, 0, []);
}
