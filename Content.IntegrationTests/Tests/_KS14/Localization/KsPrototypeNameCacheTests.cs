using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Packaging._KS14.Localization;
using Content.Shared._KS14.Localization;
using Robust.Packaging;
using Robust.Packaging.AssetProcessing;
using Robust.Packaging.AssetProcessing.Passes;

namespace Content.IntegrationTests.Tests._KS14.Localization;

public sealed class KsPrototypeNameCacheTests
{
    private static AssetFile[] Sources()
    {
        return
        [
            Text("Prototypes/test.yml", """
                - type: entity
                  id: TestBase
                  abstract: true
                  name: inherited English name
                  description: inherited English description
                - type: entity
                  id: TestChild
                  parent: TestBase
                - type: entity
                  id: TestLiteral
                  name: explicit literal
                - type: entity
                  id: TestReference
                  parent: TestBase
                - type: entity
                  id: TestEmpty
                  parent: TestBase
                - type: entity
                  id: TestReplacement
                  name: original literal
                """),
            Text("_KsModule_ReplacedPrototypes/test.yml", """
                - type: entity
                  id: TestReplacement
                  name: replaced literal
                """),
            Text("Locale/en-US/_KS14/Localization/options.ftl", "ks-ui-options-client-locale = Client language"),
            Text("Locale/fr-FR/_KS14/Localization/options.ftl", "ks-ui-options-client-locale = Langue du client"),
            Text("Locale/en-US/test.ftl", """
                ent-TestBase = English base
                    .desc = English parent description
                ent-TestReference = English reference
                    .desc = English child description
                """),
            Text("Locale/fr-FR/test.ftl", """
                ent-TestBase = base française
                    .desc = Description du parent français.
                ent-TestChild =
                    .desc = Description uniquement.
                ent-TestReference =
                    { ent-TestBase }
                ent-TestEmpty =
                    .desc = { "" }
                """),
        ];
    }

    [Test]
    public void CompilesAbstractInheritanceAttributeOnlyEntriesReferencesAndReplacements()
    {
        using var stream = new MemoryStream(KsPrototypeNameCacheCompiler.Compile(Sources()));
        var cache = KsPrototypeNameCache.Read(stream);
        Assert.Multiple(() =>
        {
            Assert.That(cache.Keys, Is.EquivalentTo(new[] { "en-US", "fr-FR" }));
            Assert.That(cache["fr-FR"].Names["TestChild"], Is.EqualTo("base française"));
            Assert.That(cache["fr-FR"].Names["TestReference"], Is.EqualTo("base française"));
            Assert.That(cache["fr-FR"].Names["TestLiteral"], Is.EqualTo("explicit literal"));
            Assert.That(cache["fr-FR"].Names["TestReplacement"], Is.EqualTo("replaced literal"));
            Assert.That(cache["fr-FR"].Descriptions["TestChild"], Is.EqualTo("Description uniquement."));
            Assert.That(cache["fr-FR"].Descriptions["TestReference"], Is.EqualTo("Description du parent français."));
            Assert.That(cache["fr-FR"].Descriptions["TestEmpty"], Is.Empty);
            Assert.That(cache["fr-FR"].Names.ContainsKey("TestBase"), Is.False);
            Assert.That(cache["en-US"].Names["TestChild"], Is.EqualTo("inherited English name"));
        });
    }

    [Test]
    public void CompilationIsIndependentOfInputOrder()
    {
        Assert.That(KsPrototypeNameCacheCompiler.Compile(Sources().Reverse()),
            Is.EqualTo(KsPrototypeNameCacheCompiler.Compile(Sources())));
    }

    [Test]
    public async Task PackagingFailureIsReportedToCaller()
    {
        var input = new AssetPassPipe { Name = "input" };
        var compiler = new KsPrototypeNameCachePass();
        compiler.AddDependency(input);
        AssetGraph.CalculateGraph([input, compiler], new PackageLoggerConsole());
        input.InjectFinished();
        await compiler.FinishedTask;
        Assert.Throws<InvalidDataException>(() => compiler.ThrowIfCompilationFailed());
    }

    [Test]
    public async Task PackagingPreservesSourcesAndReplacesStaleCache()
    {
        var input = new AssetPassPipe { Name = "input" };
        var compiler = new KsPrototypeNameCachePass();
        var collector = new CacheCollector();
        compiler.AddDependency(input).AddBefore(collector);
        collector.AddDependency(input);
        collector.AddDependency(compiler);
        AssetGraph.CalculateGraph([input, compiler, collector], new PackageLoggerConsole());
        foreach (var file in Sources())
            input.InjectFile(file);
        input.InjectFile(Text(KsPrototypeNameCache.ResourcePath, "stale cache"));
        input.InjectFinished();
        await collector.FinishedTask;
        compiler.ThrowIfCompilationFailed();
        Assert.That(collector.Files.Count, Is.EqualTo(Sources().Length + 1));
        using var stream = collector.Files.Single(file => file.Path == KsPrototypeNameCache.ResourcePath).Open();
        Assert.That(KsPrototypeNameCache.Read(stream)["fr-FR"].Names["TestChild"], Is.EqualTo("base française"));
    }

    private static AssetFile Text(string path, string contents)
        => new AssetFileMemory(path, Encoding.UTF8.GetBytes(contents));

    private sealed class CacheCollector : AssetPass
    {
        public List<AssetFile> Files { get; } = [];

        protected override AssetFileAcceptResult AcceptFile(AssetFile file)
        {
            lock (Files)
                Files.Add(file);
            return AssetFileAcceptResult.Consumed;
        }
    }
}
