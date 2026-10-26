using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenAssemblyCompiler))]
public sealed class KsProcgenCompiledContentTests : KsProcgenGameTest
{
    [TestCase("KsProcgenOfficeWorkstations")]
    [TestCase("KsProcgenOfficeStorage")]
    public async Task LoadedPacksProduceImmutableCoresWithoutRetainingEditablePrototypeLists(string packId)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var pack = manager.Index<KsProcgenEntityPackPrototype>(packId);
            var plain = pack.Entries.FirstOrDefault();
            var assembly = plain == null ? manager.Index<KsProcgenAssemblyPrototype>(pack.Assemblies.Single().Assembly) : null;
            var footprint = plain?.Footprint ?? assembly!.Members[0].Footprint;
            var sourceMemberId = plain?.Id ?? assembly!.Members[0].Id;
            var originalFootprint = footprint.ToArray();
            var originalWeight = plain?.Weight ?? pack.Assemblies.Single().Weight;
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                Assert.That(KsProcgenAssemblyCompiler.TryResolvePack(manager, pack, out var cores, out var issue), Is.True, issue?.Code);
                var core = cores.Single();
                var variant = core.Variants.First();
                var entry = variant.Members.Single(member => member.SourceMemberId == sourceMemberId).Entry;
                footprint.Add(new(1, 0));
                if (plain != null) plain.Weight += 0.5f;
                else pack.Assemblies.Single().Weight += 0.5f;
                Assert.That(entry.Footprint, Is.EqualTo(originalFootprint));
                Assert.That(core.Weight, Is.EqualTo(originalWeight));
                Assert.That(KsProcgenAssemblyCompiler.TryResolvePack(manager, pack, out var changed, out issue), Is.True, issue?.Code);
                Assert.That(changed.Single().Weight, Is.EqualTo(originalWeight + 0.5f));
                Assert.That(changed.Single().Variants.First().Members.Single(member => member.SourceMemberId == sourceMemberId).Entry.Footprint.Count,
                    Is.EqualTo(originalFootprint.Length + 1));
                Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenResolvedEntityCore>) cores).Clear());
                Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenResolvedAssembly>) core.Variants)[0] = variant);
                Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenResolvedAssemblyMember>) variant.Members).Clear());
                Assert.Throws<NotSupportedException>(() => ((IList<Vector2i>) entry.Footprint)[0] = new(2, 0));
                Assert.Throws<NotSupportedException>(() => ((IList<int>) entry.AllowedQuarterTurns).Clear());
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally
            {
                footprint.Clear(); footprint.AddRange(originalFootprint);
                if (plain != null) plain.Weight = originalWeight;
                else pack.Assemblies.Single().Weight = originalWeight;
            }
        });
    }
}
