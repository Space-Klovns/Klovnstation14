#nullable enable
using System;
using System.IO;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using YamlDotNet.RepresentationModel;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenPrototypeManifestTests
{
    [Test]
    public async Task ResolvedDeclarationsPinInheritedClientComponentsWithoutReplacingLiveBodyChecks()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(manager.TryGetMapping<EntityPrototype>("Table", out var declaration), Is.True);
            var sprite = declaration!.Get<SequenceDataNode>("components").Cast<MappingDataNode>()
                .Single(component => component.Get<ValueDataNode>("type").Value == "Sprite");
            Assert.That(SEntMan.ComponentFactory.IsIgnored("Sprite"), Is.True);
            Assert.That(manager.Index(new EntProtoId("Table")).Components.ContainsKey("Sprite"), Is.False);
            var hadColor = sprite.TryGetValue("color", out var color); var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryCapture(new[] { "Table" }, [], out var original, out var issue), Is.True, message: issue?.Code);
            var body = original!.Bodies.Single(entry => entry.PrototypeId == "Table"); Assert.That(original.SchemaVersion, Is.EqualTo(11));
            Assert.That(body.CanonicalDeclaration, Does.Contain("Sprite"));
            try
            {
                sprite["color"] = new ValueDataNode("red");
                Assert.That(system.TryCapture(new[] { "Table" }, [], out var changed, out issue), Is.True, message: issue?.Code);
                Assert.That(changed!.Bodies.Single(entry => entry.PrototypeId == "Table").CanonicalBody, Is.EqualTo(body.CanonicalBody));
                Assert.That(changed.Bodies.Single(entry => entry.PrototypeId == "Table").CanonicalDeclaration, Is.Not.EqualTo(body.CanonicalDeclaration));
                Assert.That(changed.ContentHash, Is.Not.EqualTo(original.ContentHash));
                Assert.That(system.Matches(original, out issue), Is.False); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(original.Bodies.Single(entry => entry.PrototypeId == "Table").CanonicalDeclaration, Is.EqualTo(body.CanonicalDeclaration));
            }
            finally { if (hadColor) sprite["color"] = color!; else sprite.Remove("color"); }
            Assert.That(system.Matches(original, out issue), Is.True, message: issue?.Code);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("entity")] [TestCase("tile")]
    public async Task DetachedResolvedMappingsDetectDeclarationOnlyEdits(string kind)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(system.TryCapture(new[] { "Table" }, new[] { "FloorSteel" }, out var original, out var issue), Is.True, message: issue?.Code);
            Assert.That(manager.TryGetMapping(kind == "entity" ? typeof(EntityPrototype) : typeof(ContentTileDefinition),
                kind == "entity" ? "Table" : "FloorSteel", out var declaration), Is.True);
            const string probe = "ksManifestDeclarationProbe"; Assert.That(declaration!.Has(probe), Is.False);
            try
            {
                declaration.Add(probe, new ValueDataNode("changed"));
                Assert.That(system.Matches(original!, out issue), Is.False); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(system.TryCapture(new[] { "Table" }, new[] { "FloorSteel" }, out var changed, out issue), Is.True, message: issue?.Code);
                Assert.That(changed!.Bodies.Select(body => body.CanonicalBody), Is.EqualTo(original!.Bodies.Select(body => body.CanonicalBody)));
            }
            finally { declaration.Remove(probe); }
            Assert.That(system.Matches(original!, out issue), Is.True, message: issue?.Code);
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task MappingKeyTagsFromBothEngineRepresentationsChangeIdentity(bool representationModel)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(manager.TryGetMapping<EntityPrototype>("Table", out var declaration), Is.True);
            const string probe = "ksManifestKeyProbe"; Assert.That(declaration!.Has(probe), Is.False);
            try
            {
                var firstNode = TaggedKey("!alpha", representationModel); declaration.Add(probe, firstNode);
                Assert.That(firstNode.GetKeyTag("key"), Is.EqualTo("!alpha"));
                Assert.That(system.TryCapture(new[] { "Table" }, [], out var first, out var issue), Is.True, message: issue?.Code);
                declaration[probe] = TaggedKey("!beta", representationModel);
                Assert.That(system.TryCapture(new[] { "Table" }, [], out var second, out issue), Is.True, message: issue?.Code);
                Assert.That(second!.Bodies.Single(entry => entry.PrototypeId == "Table").CanonicalBody, Is.EqualTo(first!.Bodies.Single(entry => entry.PrototypeId == "Table").CanonicalBody));
                Assert.That(second.Bodies.Single(entry => entry.PrototypeId == "Table").CanonicalDeclaration, Is.Not.EqualTo(first.Bodies.Single(entry => entry.PrototypeId == "Table").CanonicalDeclaration));
                Assert.That(second.ContentHash, Is.Not.EqualTo(first.ContentHash));
                Assert.That(system.Matches(first, out issue), Is.False); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { declaration.Remove(probe); }
        });
    }

    [Test]
    public async Task KeyTagTextConsumesTheSharedCharacterBudget()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(Pair.Server.ResolveDependency<IPrototypeManager>().TryGetMapping<EntityPrototype>("Table", out var declaration), Is.True);
            const string probe = "ksManifestKeyProbe";
            try
            {
                declaration!.Add(probe, TaggedKey("!a", representationModel: false));
                Assert.That(system.TryCapture(new[] { "Table" }, [], out var first, out var issue), Is.True, message: issue?.Code);
                declaration[probe] = TaggedKey("!" + new string('x', 128), representationModel: false);
                Assert.That(system.TryCapture(new[] { "Table" }, [], out var rejected, out issue,
                    limits: new() { MaximumCharacters = first!.Characters }), Is.False);
                Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestCharacterBudget"));
            }
            finally { declaration!.Remove(probe); }
        });
    }

    [Test]
    public async Task DeclarationRegistryOrderIsNormalizedAndOrderedDataSequencesArePreserved()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(Pair.Server.ResolveDependency<IPrototypeManager>().TryGetMapping<EntityPrototype>("Table", out var declaration), Is.True);
            var components = declaration!.Get<SequenceDataNode>("components"); var entries = components.ToArray();
            Assert.That(entries.Length, Is.GreaterThan(1)); const string probe = "ksManifestSequenceProbe";
            try
            {
                declaration.Add(probe, new SequenceDataNode(new ValueDataNode("first"), new ValueDataNode("second")));
                Assert.That(system.TryCapture(new[] { "Table" }, [], out var first, out var issue), Is.True, message: issue?.Code);
                components.Clear(); foreach (var entry in entries.Reverse()) components.Add(entry);
                Assert.That(system.Matches(first!, out issue), Is.True, message: issue?.Code);
                declaration[probe] = new SequenceDataNode(new ValueDataNode("second"), new ValueDataNode("first"));
                Assert.That(system.Matches(first!, out issue), Is.False); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { components.Clear(); foreach (var entry in entries) components.Add(entry); declaration.Remove(probe); }
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task InvalidResolvedRegistriesCannotBeIgnored(bool duplicate)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(Pair.Server.ResolveDependency<IPrototypeManager>().TryGetMapping<EntityPrototype>("Table", out var declaration), Is.True);
            var components = declaration!.Get<SequenceDataNode>("components");
            try
            {
                if (duplicate) components.Add(components[0]); else declaration["components"] = new ValueDataNode("invalid");
                Assert.That(system.TryCapture(new[] { "Table" }, [], out var result, out var issue), Is.False);
                Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("UnsupportedPrototypeManifestDeclaration"));
            }
            finally { if (duplicate) components.RemoveAt(components.Count - 1); else declaration["components"] = components; }
        });
    }

    [Test]
    public async Task IgnoredClientDeclarationDriftInvalidatesMainOwnedStage()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            Assert.That(Pair.Server.ResolveDependency<IPrototypeManager>().TryGetMapping<EntityPrototype>("WallSolid", out var declaration), Is.True);
            var sprite = declaration!.Get<SequenceDataNode>("components").Cast<MappingDataNode>()
                .Single(component => component.Get<ValueDataNode>("type").Value == "Sprite");
            var hadColor = sprite.TryGetValue("color", out var color);
            try
            {
                var result = stages.TryGenerate(Request(), "KsProcgenNoDoorThemeFixture", planningBudgets: Budgets());
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                sprite["color"] = new ValueDataNode("red");
                Assert.That(stages.Verify(result.Stage!), Is.False);
                var rejected = stages.TryInitialize(result.Stage!);
                Assert.That(rejected.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected)); Assert.That(rejected.Issue?.Code, Is.EqualTo("OwnedStagePrototypeDrift"));
                Assert.That(result.Stage!.Active, Is.False);
            }
            finally { if (hadColor) sprite["color"] = color!; else sprite.Remove("color"); stages.DiscardAll(); }
        });
    }

    [Test]
    public async Task RuntimeAndDeclarationBodiesShareOneCharacterAllowance()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture(new[] { "Table" }, [], out var original, out var issue), Is.True, message: issue?.Code);
            var body = original!.Bodies.Single(entry => entry.PrototypeId == "Table");
            var total = original.Bodies.Sum(entry => entry.CanonicalBody.Length + entry.CanonicalDeclaration.Length + entry.CanonicalMetadata.Length) +
                original.Dependencies.Sum(entry => entry.CanonicalBody.Length + entry.CanonicalDeclaration.Length) +
                original.AbstractTemplates.Sum(entry => entry.CanonicalFields.Length + entry.CanonicalInspection.Length) +
                original.Bodies.Where(entry => entry.Kind == KsProcgenPrototypeBodyKind.Tile).Sum(entry => entry.CanonicalBody.Length);
            Assert.That(original.Characters, Is.EqualTo(total));
            var single = Math.Max(body.CanonicalBody.Length, body.CanonicalDeclaration.Length);
            Assert.That(total, Is.GreaterThan(single));
            Assert.That(system.TryCapture(new[] { "Table" }, [], out var rejected, out issue,
                limits: new() { MaximumCharacters = single }), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestCharacterBudget"));
        });
    }

    [Test]
    public async Task RegistryNormalizationPreservesTheSequenceTag()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(Pair.Server.ResolveDependency<IPrototypeManager>().TryGetMapping<EntityPrototype>("Table", out var declaration), Is.True);
            var original = declaration!.Get<SequenceDataNode>("components");
            try
            {
                declaration["components"] = new SequenceDataNode(original.ToArray()) { Tag = "!registry-alpha" };
                Assert.That(system.TryCapture(new[] { "Table" }, [], out var first, out var issue), Is.True, message: issue?.Code);
                declaration["components"] = new SequenceDataNode(original.ToArray()) { Tag = "!registry-beta" };
                Assert.That(system.Matches(first!, out issue), Is.False); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { declaration["components"] = original; }
        });
    }

    private static MappingDataNode TaggedKey(string tag, bool representationModel) => representationModel
        ? new MappingDataNode(new YamlMappingNode { { new YamlScalarNode("key") { Tag = tag }, new YamlScalarNode("value") } })
        : (MappingDataNode) DataNodeParser.ParseYamlStream(new StringReader(tag + " key: value\n")).Single().Root;
}
