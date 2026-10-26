#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenPrototypeManifestTests
{
    private ContentTileDefinition ReloadTile()
    {
        var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
        var tiles = Pair.Server.ResolveDependency<ITileDefinitionManager>();
        manager.ReloadPrototypes(new Dictionary<Type, HashSet<string>>
            { [typeof(ContentTileDefinition)] = new() { "FloorSteel" } });
        var prototype = manager.Index(new ProtoId<ContentTileDefinition>("FloorSteel"));
        Assert.That(prototype, Is.Not.SameAs(tiles["FloorSteel"]));
        Assert.That(prototype.TileId, Is.EqualTo(tiles["FloorSteel"].TileId));
        return prototype;
    }

    [Test]
    public async Task EquivalentReloadedTilesRetainIdentityAndCanMaterializeAnOwnedScene()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture([], new[] { "FloorSteel" }, out var before, out var issue), Is.True, message: issue?.Code);
            var entities = SEntMan.GetEntities().ToHashSet(); ReloadTile();
            Assert.That(system.TryCapture([], new[] { "FloorSteel" }, out var after, out issue), Is.True, message: issue?.Code);
            Assert.That(after!.Bodies, Is.EqualTo(before!.Bodies)); Assert.That(after.ContentHash, Is.EqualTo(before.ContentHash));
            Assert.That(after.Nodes, Is.EqualTo(before.Nodes)); Assert.That(after.Characters, Is.EqualTo(before.Characters));
            Assert.That(system.Matches(before, out issue), Is.True, message: issue?.Code);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(entities));
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            try
            {
                var result = stages.TryGenerate(Request(), "Manifest", planningBudgets: Budgets(), content: Catalog());
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                Assert.That(stages.Verify(result.Stage!), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(entities));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task MatchingTileIdsCannotHideConflictingLoadedOrRegisteredBodies(bool registeredBody)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var prototype = ReloadTile();
            var registered = (ContentTileDefinition) Pair.Server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"];
            Assert.That(system.TryCapture([], new[] { "FloorSteel" }, out var original, out var issue), Is.True, message: issue?.Code);
            var target = registeredBody ? registered : prototype; var mass = target.Mass;
            var entities = SEntMan.GetEntities().ToHashSet();
            try
            {
                target.Mass += 1f;
                Assert.That(system.TryCapture([], new[] { "FloorSteel" }, out var manifest, out issue), Is.False);
                Assert.That(manifest, Is.Null); Assert.That(issue?.Code, Is.EqualTo("UnsupportedPrototypeManifestTileBinding"));
                Assert.That(system.Matches(original!, out issue), Is.False); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { target.Mass = mass; }
            Assert.That(system.Matches(original!, out issue), Is.True, message: issue?.Code);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(entities));
        });
    }

    [TestCase("loaded")] [TestCase("registered")] [TestCase("both")]
    public async Task RuntimeTileIdsMustAgreeWithBothNamedAndNumericRegistrations(string kind)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var prototype = ReloadTile();
            var tiles = Pair.Server.ResolveDependency<ITileDefinitionManager>(); var registered = (ContentTileDefinition) tiles["FloorSteel"];
            var id = registered.TileId; Assert.That((int) id + 1, Is.LessThan(tiles.Count));
            try
            {
                if (kind != "registered") prototype.AssignTileId((ushort) ((int) id + 1));
                if (kind != "loaded") registered.AssignTileId((ushort) ((int) id + 1));
                Assert.That(system.TryCapture([], new[] { "FloorSteel" }, out var manifest, out var issue), Is.False);
                Assert.That(manifest, Is.Null); Assert.That(issue?.Code, Is.EqualTo("UnsupportedPrototypeManifestTileBinding"));
            }
            finally { prototype.AssignTileId(id); registered.AssignTileId(id); }
        });
    }

    [TestCase("characters")] [TestCase("nodes")]
    public async Task RegisteredTileComparisonUsesTheSameAggregateStructuralBudget(string kind)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); ReloadTile();
            Assert.That(system.TryCapture([], new[] { "FloorSteel" }, out var original, out var issue), Is.True, message: issue?.Code);
            var body = original!.Bodies.Single(entry => entry.Kind == KsProcgenPrototypeBodyKind.Tile && entry.PrototypeId == "FloorSteel");
            var total = original.Bodies.Sum(entry => entry.CanonicalBody.Length + entry.CanonicalDeclaration.Length + entry.CanonicalMetadata.Length) +
                original.Dependencies.Sum(entry => entry.CanonicalBody.Length + entry.CanonicalDeclaration.Length) +
                original.AbstractTemplates.Sum(entry => entry.CanonicalFields.Length + entry.CanonicalInspection.Length);
            var registeredComparisons = original.Bodies.Where(entry => entry.Kind == KsProcgenPrototypeBodyKind.Tile).Sum(entry => entry.CanonicalBody.Length);
            Assert.That(original.Characters, Is.EqualTo(total + registeredComparisons));
            var limits = kind == "characters"
                ? original.Limits with { MaximumCharacters = total + registeredComparisons - body.CanonicalBody.Length }
                : original.Limits with { MaximumNodes = original.Nodes - 1 };
            Assert.That(system.TryCapture([], new[] { "FloorSteel" }, out var manifest, out issue, limits: limits), Is.False);
            Assert.That(manifest, Is.Null);
            Assert.That(issue?.Code, Is.EqualTo(kind == "characters" ? "PrototypeManifestCharacterBudget" : "PrototypeManifestNodeBudget"));
        });
    }

    [Test]
    public async Task EqualChangesToBothTileBodiesStillInvalidateTheCapturedManifest()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var prototype = ReloadTile();
            var registered = (ContentTileDefinition) Pair.Server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"];
            Assert.That(system.TryCapture([], new[] { "FloorSteel" }, out var original, out var issue), Is.True, message: issue?.Code);
            var mass = prototype.Mass; var registeredMass = registered.Mass;
            try
            {
                prototype.Mass += 1f; registered.Mass += 1f;
                Assert.That(system.TryCapture([], new[] { "FloorSteel" }, out var changed, out issue), Is.True, message: issue?.Code);
                Assert.That(changed!.ContentHash, Is.Not.EqualTo(original!.ContentHash));
                Assert.That(system.Matches(original, out issue), Is.False); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { prototype.Mass = mass; registered.Mass = registeredMass; }
        });
    }
}
