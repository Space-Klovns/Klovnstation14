using System.Linq;
using Content.Shared._KS14.Anchorless.Components;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Anchorless;

[TestFixture]
public sealed class AnchorlessIdentityHelperTests
{
    [Test]
    public void MergeIdentityData_UnionsEntriesFromBothSides()
    {
        var first = new AnchorlessIdentityData
        {
            OriginalName = "Alice",
            Starting = true,
        };

        var second = new AnchorlessIdentityData
        {
            OriginalName = "Bob",
            Starting = false,
        };

        var merged = AnchorlessIdentityHelper.MergeIdentityData(new[] { first }, new[] { second });

        Assert.That(merged.Select(entry => entry.OriginalName), Is.EquivalentTo(new[] { "Alice", "Bob" }));
    }
}
