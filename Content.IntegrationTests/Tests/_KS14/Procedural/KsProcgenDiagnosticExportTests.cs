#nullable enable
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Console;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenDiagnosticExport))]
public sealed class KsProcgenDiagnosticExportTests : GameTest
{
    [Test]
    public async Task SnapshotCommandRegistersAndExportsPaintWithoutGeneratingAPlan()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var command = Pair.Server.ResolveDependency<IConsoleHost>().AvailableCommands["ksprocgen_snapshot"];
            Assert.That(command.Description, Is.Not.Empty);
            Assert.That(command.Help, Is.Not.Empty);
            var snapshot = KsProcgenAreaNormalizer.Normalize("Host",
                new[] { new KsProcgenAreaMarker(new(0, 0), "Main", "Profile") },
                new[] { new KsProcgenAreaProfile("Profile", KsProcgenMode.Procedural, KsProcgenGeometryMode.Footprint,
                    KsProcgenConnectivityPolicy.PerIsland, "KsProcgenSimpleOffice", 64, 100) });
            var exported = KsProcgenDiagnosticExport.SerializeArea(snapshot);
            Assert.That(exported.Status, Is.EqualTo(KsProcgenDiagnosticExportStatus.Complete));
            using var json = JsonDocument.Parse(exported.Data);
            Assert.That(json.RootElement.GetProperty("kind").GetString(), Is.EqualTo("AreaSnapshot"));
            Assert.That(json.RootElement.GetProperty("planGenerated").GetBoolean(), Is.False);
            Assert.That(json.RootElement.GetProperty("engineContextVerified").GetBoolean(), Is.False);
            Assert.That(json.RootElement.GetProperty("blobs").GetArrayLength(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task LoadedLShapeExportsCanonicalJsonWithoutEngineWrites()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var request = Request();
            var result = KsProcgenOwnedInteriorPlanner.Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), request,
                "KsProcgenEmptyOfficeThemeFixture", new(new System.Collections.Generic.HashSet<Vector2i>(),
                    new System.Collections.Generic.HashSet<Vector2i> { new(-1, 0), new(-2, 0) },
                    new System.Collections.Generic.HashSet<Vector2i>(), true, true), [],
                budgets: new() { UseSizeGoals = true, MaximumPartitionMerges = 0, MaximumLayoutProbes = 1 });
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var report = KsProcgenOwnedDiagnosticReportBuilder.Build(request, result);
            var json = KsProcgenDiagnosticExport.Serialize(report);
            Assert.That(json.Status, Is.EqualTo(KsProcgenDiagnosticExportStatus.Complete), json.Issue?.Code);
            using var document = JsonDocument.Parse(json.Data);
            Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(3));
            Assert.That(document.RootElement.GetProperty("published").GetBoolean(), Is.False);
            Assert.That(document.RootElement.GetProperty("engineAccessVerified").GetBoolean(), Is.False);
            Assert.That(document.RootElement.GetProperty("floorCells").GetArrayLength(), Is.EqualTo(25));
            Assert.That(document.RootElement.GetProperty("rooms").GetArrayLength(), Is.EqualTo(2));
            Assert.That(KsProcgenDiagnosticExport.VerifyExact(json.Data, report).Status, Is.EqualTo(KsProcgenDiagnosticExportStatus.Complete));
            var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json.Data).Replace("\"published\": false", "\"published\": true"));
            Assert.That(KsProcgenDiagnosticExport.VerifyExact(tampered, report).Status, Is.EqualTo(KsProcgenDiagnosticExportStatus.Mismatch));
            var culture = CultureInfo.CurrentCulture;
            try
            {
                var changed = (CultureInfo) culture.Clone();
                changed.NumberFormat.NegativeSign = "~";
                CultureInfo.CurrentCulture = changed;
                Assert.That(KsProcgenDiagnosticExport.Serialize(report).Data, Is.EqualTo(json.Data));
            }
            finally { CultureInfo.CurrentCulture = culture; }
            var sampleDirectory = System.Environment.GetEnvironmentVariable("KS_PROCGEN_ARTIFACT_DIR");
            if (!string.IsNullOrWhiteSpace(sampleDirectory))
            {
                Directory.CreateDirectory(sampleDirectory);
                File.WriteAllBytes(Path.Combine(sampleDirectory, "l-shape.json"), json.Data);
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ExportByteLimitsRejectWithoutPartialDataAndNamesRoundtripAsJsonText()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var report = new KsProcgenOwnedDiagnosticReport
                { RequestId = "<script>alert(1)</script>", Status = "Rejected", RequestedCells = new[] { new KsProcgenDiagnosticCell(0, 0) } };
            var limited = KsProcgenDiagnosticExport.Serialize(report, maximumBytes: 1);
            Assert.That(limited.Status, Is.EqualTo(KsProcgenDiagnosticExportStatus.BudgetExceeded));
            Assert.That(limited.Data, Is.Empty);
            var exported = KsProcgenDiagnosticExport.Serialize(report);
            using var json = JsonDocument.Parse(exported.Data);
            Assert.That(json.RootElement.GetProperty("requestId").GetString(), Is.EqualTo(report.RequestId));
        });
    }

    private static KsProcgenRequest Request() => new()
    {
        RequestId = "LShapeDiagnostic", Seed = -17,
        Shape = new() { Cells = Enumerable.Range(0, 8).SelectMany(x => Enumerable.Range(0, x >= 6 ? 2 : 4)
            .Select(y => new Vector2i(x, y))).ToList() },
        SizeMix = new() { new() { Id = "Large", MinCells = 16, MaxCells = 16 }, new() { Id = "Small", MinCells = 9, MaxCells = 9 } },
        EntranceDomain = new()
        {
            Entrances = new() { new() { PortId = "Entry", ThresholdCells = new() { new(-1, 0) }, InwardNormal = new(1, 0) } },
            Configurations = new() { new() { Id = "Connected", Groups = new() { new() { Id = "Room", Ports = new() { "Entry" } } } } },
        },
    };
}
