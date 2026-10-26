#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._KS14.Procedural;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenPrototypeManifestSystem))]
public sealed class KsProcgenCapturedNodeTests : KsProcgenGameTest
{
    [TestCase("CableHV")] [TestCase("CableMV")] [TestCase("CableApcExtension")]
    [TestCase("SubstationBasic")] [TestCase("APCBasic")]
    public async Task InputOnlyNodesSurviveSnapshotsAndSpawnAsIndependentOwnedInstances(string prototypeId)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            var live = SProtoMan.Index(new EntProtoId(prototypeId));
            var declarations = ((NodeContainerComponent) live.Components["NodeContainer"].Component).Nodes;
            Assert.That(declarations, Is.Not.Empty);
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryCapture(new[] { prototypeId }, [], out var manifest, out var issue), Is.True, issue?.Code);
            Assert.That(system.TryCreatePrototypeSnapshot(manifest!, prototypeId, out var snapshot, out issue), Is.True, issue?.Code);
            var captured = ((NodeContainerComponent) snapshot!.Components["NodeContainer"].Component).Nodes;
            Assert.That(captured.Keys, Is.EquivalentTo(declarations.Keys));
            var maps = Pair.Server.System<SharedMapSystem>();
            var map = maps.CreateMap(out var mapId, runMapInit: false); maps.SetPaused(mapId, true);
            try
            {
                var first = system.SpawnCaptured(manifest!, prototypeId, new EntityCoordinates(map, Vector2.Zero));
                var firstNodes = SEntMan.GetComponent<NodeContainerComponent>(first).Nodes;
                var metadataNodes = ((NodeContainerComponent) SEntMan.GetComponent<MetaDataComponent>(first)
                    .EntityPrototype!.Components["NodeContainer"].Component).Nodes;
                var second = system.SpawnCaptured(manifest!, prototypeId, new EntityCoordinates(map, Vector2.UnitX));
                var secondNodes = SEntMan.GetComponent<NodeContainerComponent>(second).Nodes;
                Assert.That(firstNodes.Keys, Is.EquivalentTo(declarations.Keys));
                Assert.That(secondNodes.Keys, Is.EquivalentTo(declarations.Keys));
                foreach (var (name, declaration) in declarations)
                {
                    Assert.That(captured[name].GetType(), Is.EqualTo(declaration.GetType()));
                    Assert.That(captured[name].NodeGroupID, Is.EqualTo(declaration.NodeGroupID));
                    Assert.That(captured[name], Is.Not.SameAs(declaration));
                    Assert.That(firstNodes[name], Is.Not.SameAs(declaration).And.Not.SameAs(captured[name])
                        .And.Not.SameAs(metadataNodes[name]).And.Not.SameAs(secondNodes[name]));
                    Assert.That(firstNodes[name].Owner, Is.EqualTo(first));
                    Assert.That(secondNodes[name].Owner, Is.EqualTo(second));
                    Assert.That(firstNodes[name].NodeGroupID, Is.EqualTo(declaration.NodeGroupID));
                    Assert.That(secondNodes[name].GetType(), Is.EqualTo(declaration.GetType()));
                }
                metadataNodes.Clear();
                var third = system.SpawnCaptured(manifest!, prototypeId, new EntityCoordinates(map, Vector2.UnitY));
                Assert.That(SEntMan.GetComponent<NodeContainerComponent>(third).Nodes.Keys, Is.EquivalentTo(declarations.Keys));
                Assert.That(system.Matches(manifest!, out issue), Is.True, issue?.Code);
            }
            finally { maps.DeleteMap(mapId); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }
}
