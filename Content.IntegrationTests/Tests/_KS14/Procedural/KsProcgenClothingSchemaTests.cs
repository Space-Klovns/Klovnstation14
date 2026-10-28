#nullable enable
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed class KsProcgenClothingSchemaTests : KsProcgenGameTest
{
    [TestCase("UnsensoredClothingUniformSkirtBase")]
    [TestCase("ClothingUniformSkirtBase")]
    [TestCase("BaseTowel")]
    public async Task MigratedClothingDeclarationsHaveNoMaskGapAndStillReportUnknownFields(string id)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(SProtoMan.TryGetMapping<EntityPrototype>(id, out var declaration), Is.True);
            var clothing = declaration!.Get<SequenceDataNode>("components").Cast<MappingDataNode>()
                .Single(node => node.Get<ValueDataNode>("type").Value == "Clothing");
            Assert.That(clothing.Has("femaleMask"), Is.False);
            Assert.That(system.TryCaptureWithReferences([], [], [id], out var captured, out var issue), Is.True, issue?.ToString());
            var template = captured!.AbstractTemplates.Single(value => value.Kind == "entity" && value.PrototypeId == id);
            Assert.That(template.HasDeclaredInspectionGaps, Is.False, template.CanonicalInspection);
            clothing.Add("ksProcgenUnknownClothingField", new ValueDataNode("must-retain-this-data"));
            try
            {
                Assert.That(system.TryCaptureWithReferences([], [], [id], out var changed, out issue), Is.True, issue?.ToString());
                template = changed!.AbstractTemplates.Single(value => value.Kind == "entity" && value.PrototypeId == id);
                Assert.That(template.HasDeclaredInspectionGaps, Is.True);
                Assert.That(template.CanonicalInspection, Does.Contain("ksProcgenUnknownClothingField"));
                Assert.That(template.CanonicalFields, Does.Contain("must-retain-this-data"));
                Assert.That(changed.ContentHash, Is.Not.EqualTo(captured.ContentHash));
                Assert.That(changed.FullContentClosureVerified, Is.False);
            }
            finally { clothing.Remove("ksProcgenUnknownClothingField"); }
        });
    }

    [TestCase("Clothing/Uniforms/Jumpsuit/atmos.rsi")]
    [TestCase("/Textures/Clothing/Uniforms/Jumpsuit/atmos.rsi")]
    [TestCase(null)]
    public async Task ClothingSpriteStringApiAndYamlRepresentationRoundtripUnchanged(string? path)
    {
        await AssertOnServer(() =>
        {
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            var input = new MappingDataNode
            {
                { "slots", new SequenceDataNode { new ValueDataNode("INNERCLOTHING") } },
                { "sprite", path == null ? ValueDataNode.Null() : new ValueDataNode(path) },
            };
            var original = serialization.Read<ClothingComponent>(input, skipHook: true, notNullableOverride: true);
            var node = (MappingDataNode) serialization.WriteValue(original, alwaysWrite: true, notNullableOverride: true);
            var copy = serialization.Read<ClothingComponent>(node, skipHook: true, notNullableOverride: true);
            Assert.That(copy.RsiPath, Is.EqualTo(path));
            Assert.That(copy.Slots, Is.EqualTo(original.Slots));
            Assert.That(node.Get<ValueDataNode>("sprite").IsNull, Is.EqualTo(path == null));
            if (path != null) Assert.That(node.Get<ValueDataNode>("sprite").Value, Is.EqualTo(path));
        });
    }

    [Test]
    public async Task StandardOfficeHasNoRemainingRegisteredDeclarationSchemaGaps()
    {
        await AssertOnServer(() =>
        {
            Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "KsProcgenSimpleOffice", out var catalog, out var issue), Is.True, issue?.ToString());
            Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(catalog!, out var manifest, out issue), Is.True, issue?.ToString());
            Assert.That(manifest!.AbstractTemplates.Where(template => template.HasDeclaredInspectionGaps), Is.Empty);
            Assert.That(manifest.FullContentClosureVerified, Is.False,
                "A clean registered schema is not proof of ignored client/custom resource coverage.");
        });
    }
}
