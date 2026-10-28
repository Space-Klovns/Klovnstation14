using System.Collections.Generic;
using System.Linq;
using Content.Server.Power.Pow3r;
using NUnit.Framework;

namespace Content.Tests._KS14.Power;

[TestFixture]
public sealed class KsPowerStateStorageTests
{
    [TestCase(4)] [TestCase(8)] [TestCase(16)]
    public void CapacityEdgesAllocateValidGenerationsAndCanBeRestored(int count)
    {
        var storage = new PowerState.GenIdStorage<object>();
        var entries = new List<(PowerState.NodeId, object)>();
        for (var index = 0; index < count; index++)
        {
            var value = new object();
            storage.Allocate(out var id) = value;
            Assert.That(id.Generation, Is.GreaterThan(0), $"new slot {index} cannot use the unallocated generation");
            entries.Add((id, value));
        }
        var restored = PowerState.GenIdStorage<object>.FromEnumerable(entries);
        foreach (var (id, value) in entries)
        {
            Assert.That(storage[id], Is.SameAs(value));
            Assert.That(restored[id], Is.SameAs(value));
        }
        Assert.That(restored.Values.ToArray(), Is.EquivalentTo(entries.Select(entry => entry.Item2)));
    }

    [Test]
    public void ReusingTheLastSlotInvalidatesItsPreviousIdentity()
    {
        var storage = new PowerState.GenIdStorage<object>();
        var lastId = default(PowerState.NodeId);
        for (var index = 0; index < 4; index++) storage.Allocate(out lastId) = new object();
        storage.Free(lastId);
        storage.Allocate(out var replacementId) = new object();
        Assert.That(replacementId.Index, Is.EqualTo(lastId.Index));
        Assert.That(replacementId.Generation, Is.EqualTo(lastId.Generation + 1));
        Assert.Throws<KeyNotFoundException>(() => { _ = storage[lastId]; });
        Assert.That(storage.Count, Is.EqualTo(4));
    }
}
