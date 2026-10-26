#nullable enable
using System.Linq;
using Content.Server._KS14.Procedural;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenCanonicalImageTests
{
    [Test]
    public void CanonicalImagesRetainTagsNullFlagsKeyTagsAndOrderedTextWithoutYamlLoss()
    {
        var values = new SequenceDataNode { Tag = "!sequence", IsNull = true };
        values.Add(new ValueDataNode("z")); values.Add(new ValueDataNode("a"));
        values.Add(new ValueDataNode("null") { IsNull = false, Tag = "!text" }); values.Add(ValueDataNode.Null());
        values.Add(new ValueDataNode("α:\n;雪"));
        var source = new MappingDataNode { Tag = "!mapping", IsNull = true };
        source.Add("key:;α", values); source.SetKeyTag("key:;α", "!tag-雪");
        var text = Encode(source);
        Assert.That(KsProcgenPrototypeManifestSystem.TryDecodeCanonicalBody(text, new(), out var result, out var issue), Is.True, message: issue);
        var mapping = (MappingDataNode) result!;
        Assert.That(mapping, Is.Not.SameAs(source)); Assert.That(mapping.IsNull, Is.True);
        Assert.That(mapping.Tag, Is.EqualTo(source.Tag)); Assert.That(mapping.GetKeyTag("key:;α"), Is.EqualTo("!tag-雪"));
        var decoded = mapping.Get<SequenceDataNode>("key:;α"); Assert.That(decoded, Is.Not.SameAs(values));
        Assert.That(decoded.Tag, Is.EqualTo("!sequence")); Assert.That(decoded.IsNull, Is.True);
        Assert.That(decoded.Cast<ValueDataNode>().Select(value => value.Value), Is.EqualTo(values.Cast<ValueDataNode>().Select(value => value.Value)));
        Assert.That(decoded[2].IsNull, Is.False); Assert.That(decoded[2].Tag, Is.EqualTo("!text")); Assert.That(decoded[3].IsNull, Is.True);
        Assert.That(Encode(mapping), Is.EqualTo(text));
    }

    [TestCase("")]
    [TestCase("N;0V1:")]
    [TestCase("N;0VN;")]
    [TestCase("N;0S2147483648:")]
    [TestCase("N;0S999999999:")]
    [TestCase("N;2V0:")]
    [TestCase("N;0X0:")]
    [TestCase("N;0V00:")]
    [TestCase("N;0V0:garbage")]
    [TestCase("N;0M2:N;1:bN;0V0:N;1:aN;0V0:")]
    [TestCase("N;0M2:N;1:aN;0V0:N;1:aN;0V0:")]
    [TestCase("N;0M1:N;N;N;0V0:")]
    public void InvalidOrAlternateEncodingsProduceNoPartialImage(string text)
    {
        Assert.That(KsProcgenPrototypeManifestSystem.TryDecodeCanonicalBody(text, new(), out var image, out var issue), Is.False);
        Assert.That(image, Is.Null); Assert.That(issue, Is.Not.Null);
    }

    [TestCase("nodes")] [TestCase("depth")] [TestCase("characters")] [TestCase("limits")]
    public void ReconstructionHonorsStructuralAllowances(string kind)
    {
        DataNode node = new ValueDataNode("payload");
        for (var index = 0; index < 8; index++) { var parent = new SequenceDataNode(); parent.Add(node); node = parent; }
        if (kind == "nodes")
        {
            var nested = new SequenceDataNode(); nested.Add(new ValueDataNode("nested"));
            var root = new SequenceDataNode(); root.Add(nested); root.Add(new ValueDataNode("sibling")); node = root;
        }
        var text = Encode(node); var limits = new KsProcgenPrototypeManifestLimits();
        limits = kind switch { "nodes" => limits with { MaximumNodes = 3 }, "depth" => limits with { MaximumDepth = 2 },
            "characters" => limits with { MaximumCharacters = text.Length - 1 }, _ => limits with { MaximumDefinitions = 0 } };
        Assert.That(KsProcgenPrototypeManifestSystem.TryDecodeCanonicalBody(text, limits, out var image, out var issue, out var visited), Is.False);
        Assert.That(image, Is.Null); Assert.That(issue, Is.Not.Null);
        if (kind == "nodes") Assert.That(visited, Is.LessThanOrEqualTo(limits.MaximumNodes));
        if (kind == "depth") Assert.That(visited, Is.LessThanOrEqualTo(limits.MaximumDepth + 1));
        if (kind == "characters") Assert.That(visited, Is.Zero);
    }

    private static string Encode(DataNode node)
    {
        Assert.That(KsProcgenPrototypeManifestSystem.TryCanonicalBody(node, new(), out var result, out var issue, out _, out _), Is.True, message: issue);
        return result!;
    }
}
