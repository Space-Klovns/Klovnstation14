#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenElectricalPlanningContext))]
public sealed class KsProcgenElectricalPlanningTests : KsProcgenGameTest
{
    [TestCase(false)] [TestCase(true)]
    public async Task PrimaryCandidateKeepsMandatoryConvertersAndCardinalTierGraphs(bool authored)
    {
        await AssertOnServer(() =>
        {
            var request = Request(); var before = SEntMan.GetEntities().ToHashSet();
            var result = Propose(request, authored: authored); Candidate(result);
            var electrical = result.Electrical!;
            Assert.That(electrical.Devices.Single(device => !device.Substation).WallMounted, Is.True);
            Assert.That(electrical.Devices.Single(device => device.Substation).WallMounted, Is.False);
            Assert.That(electrical.Devices.Count, Is.EqualTo(2));
            Assert.That(electrical.Devices.Count(device => device.Substation), Is.EqualTo(1));
            foreach (var device in electrical.Devices)
            {
                Assert.That(electrical.Cables.Any(cable => cable.Cell == device.Cell && cable.Voltage == KsProcgenElectricalVoltage.Medium), Is.True);
                Assert.That(electrical.Cables.Any(cable => cable.Cell == device.Cell && cable.Voltage ==
                    (device.Substation ? KsProcgenElectricalVoltage.High : KsProcgenElectricalVoltage.Low)), Is.True);
            }
            var bindings = result.Furnishings.SelectMany(room => room.Proposal.Layered?.Bindings ?? []).ToArray();
            Assert.That(bindings.Count(binding => binding.Origin == KsProcgenRoomMemberOrigin.Infrastructure), Is.EqualTo(2));
            Assert.That(bindings.Count(binding => binding.Origin == KsProcgenRoomMemberOrigin.Authored), Is.EqualTo(authored ? 1 : 0));
            foreach (var voltage in new[] { KsProcgenElectricalVoltage.Medium, KsProcgenElectricalVoltage.Low })
            {
                var cells = electrical.Cables.Where(cable => cable.Voltage == voltage).Select(cable => cable.Cell).ToHashSet();
                var seen = new HashSet<Vector2i>(); var queue = new Queue<Vector2i>(); queue.Enqueue(cells.First());
                while (queue.TryDequeue(out var cell))
                {
                    if (!seen.Add(cell)) continue;
                    foreach (var offset in new[] { new Vector2i(1, 0), new(0, 1), new(-1, 0), new(0, -1) })
                        if (cells.Contains(cell + offset) && !seen.Contains(cell + offset)) queue.Enqueue(cell + offset);
                }
                Assert.That(seen, Is.EquivalentTo(cells));
            }
            if (authored)
            {
                var computer = electrical.Consumers.Single(consumer => consumer.PrototypeId == "KsProcgenFilteredComputerFixture");
                Assert.That((computer.Position - Center(computer.ProviderCell)).Length(), Is.LessThanOrEqualTo(3f));
                Assert.That(electrical.Cables.Any(cable => cable.Voltage == KsProcgenElectricalVoltage.Low && cable.Cell == computer.ProviderCell), Is.True);
                Assert.That(computer.NominalWatts, Is.GreaterThan(0f));
            }
            Assert.That(Propose(request, authored: authored).SemanticHash, Is.EqualTo(result.SemanticHash));
            Assert.That(electrical.ElectricalTopologyVerified || electrical.ElectricalSupplyVerified || result.Published, Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task EmptyUtilityLayerMaskCannotLeakAnyCandidateArtifacts(bool cables)
    {
        await AssertOnServer(() =>
        {
            var request = Request(); request.Utilities = new();
            if (cables) request.Utilities.CableWriteCells = new(); else request.Utilities.DeviceWriteCells = new();
            var before = SEntMan.GetEntities().ToHashSet(); var result = Propose(request);
            Assert.That(result.Status, Is.Not.EqualTo(KsProcgenOwnedPlanStatus.Candidate));
            Assert.That(result.Electrical, Is.Null); Assert.That(result.Furnishings, Is.Empty);
            Assert.That(result.SemanticHash, Is.Zero); Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task DevicePermissionsArePartOfJointPlacementSearch()
    {
        await AssertOnServer(() =>
        {
            var request = Request(); var baseline = Propose(request); Candidate(baseline);
            var devices = baseline.Electrical!.Devices; var changed = devices[0].Cell;
            var allowed = request.Shape.AddRectangles.Single();
            request.Utilities = new() { DeviceWriteCells = new() };
            for (var x = allowed.Min.X; x < allowed.Max.X; x++)
            for (var y = allowed.Min.Y; y < allowed.Max.Y; y++)
                if (new Vector2i(x, y) != changed) request.Utilities.DeviceWriteCells.Add(new(x, y));
            request.Utilities.DeviceWriteCells.AddRange(request.Shape.EnvelopeCells.Where(cell => cell != changed));
            var result = Propose(request); Candidate(result);
            Assert.That(result.Electrical!.Devices.All(device => device.Cell != changed), Is.True);
            Assert.That(result.Furnishings.SelectMany(room => room.Proposal.Layered?.Access.Footprints ?? [])
                .Where(footprint => result.Furnishings.SelectMany(room => room.Proposal.Layered?.Bindings ?? [])
                    .Any(binding => binding.MemberId == footprint.MemberId && binding.Origin == KsProcgenRoomMemberOrigin.Infrastructure))
                .All(footprint => footprint.Cells.All(cell => request.Utilities.DeviceWriteCells.Contains(cell))), Is.True);
            Assert.That(result.SemanticHash, Is.Not.EqualTo(baseline.SemanticHash));
        });
    }

    [Test]
    public async Task PrimaryStageMaterializesElectricalCandidateWithDeferredEvidence()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var result = Pair.Server.System<KsProcgenOwnedStageSystem>().TryGenerate(Request(), "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), result.Issue?.Code);
                Assert.That(result.Proposal!.Electrical, Is.Not.Null);
                Assert.That(result.Stage!.InitialElectrical, Is.Null);
                var initialized = Pair.Server.System<KsProcgenOwnedStageSystem>().TryInitialize(result.Stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), initialized.Issue?.Code);
                Assert.That(result.Stage.InitialElectrical!.ElectricalTopologyVerified, Is.True);
                Assert.That(result.Stage.InitialElectrical.ElectricalSupplyVerified, Is.False);
            }
            finally { Pair.Server.System<KsProcgenOwnedStageSystem>().DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("missing", "ElectricalConsumerMinimumUnmet")]
    [TestCase("room", "UnknownElectricalConsumerRoom")]
    [TestCase("domain", "ElectricalGasDomainScopeUnsupported")]
    [TestCase("operational", "ElectricalSupplyObservationRequired")]
    [TestCase("circuit", "UnknownConsumerCircuit")]
    [TestCase("loads", "ConflictingElectricalConsumerLoads")]
    [TestCase("peak", "ConflictingElectricalConsumerLoads")]
    public async Task RequiredConsumerDeclarationsCannotBeWeakened(string scenario, string expected)
    {
        await AssertOnServer(() =>
        {
            var request = Request(); request.Utilities = new();
            var consumer = new KsProcgenElectricalConsumerSpec { Id = "Computer", Supply = KsProcgenElectricalSupply.WiredOnly };
            request.Utilities.Consumers.Add(consumer);
            switch (scenario)
            {
                case "missing": consumer.Selector = KsProcgenConsumerSelector.Prototype; consumer.SelectorId = "Paper"; break;
                case "room": consumer.Scope = KsProcgenUtilityScope.Room; consumer.ScopeId = "AbsentRoom"; break;
                case "domain": consumer.Scope = KsProcgenUtilityScope.GasDomain; consumer.ScopeId = "AbsentDomain"; break;
                case "operational": consumer.Supply = KsProcgenElectricalSupply.Operational; break;
                case "circuit": consumer.CircuitId = "Other"; break;
                case "peak":
                    consumer.NominalWatts = 100f;
                    request.Utilities.Consumers.Add(new() { Id = "Peak", PeakWatts = 50f, Supply = KsProcgenElectricalSupply.WiredOnly });
                    break;
                case "loads":
                    consumer.NominalWatts = 100f;
                    request.Utilities.Consumers.Add(new() { Id = "Second", NominalWatts = 200f, Supply = KsProcgenElectricalSupply.WiredOnly });
                    break;
            }
            var result = Propose(request, authored: true);
            Assert.That(result.Status, Is.EqualTo(scenario == "circuit" ? KsProcgenOwnedPlanStatus.InvalidInput : KsProcgenOwnedPlanStatus.Rejected), result.Issue?.Code);
            Assert.That(result.Issue?.Code, Is.EqualTo(expected));
            Assert.That(result.Electrical, Is.Null); Assert.That(result.Furnishings, Is.Empty); Assert.That(result.SemanticHash, Is.Zero);
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task PreferredConsumersRetainExplicitOmissions(bool operational)
    {
        await AssertOnServer(() =>
        {
            var request = Request(); request.Utilities = new() { Consumers = new() { new()
            {
                Id = "Preference", Requirement = KsProcgenUtilityRequirement.Preferred,
                Supply = operational ? KsProcgenElectricalSupply.Operational : KsProcgenElectricalSupply.WiredOnly,
                Selector = operational ? KsProcgenConsumerSelector.AllPowered : KsProcgenConsumerSelector.Prototype,
                SelectorId = operational ? null : "Paper",
            } } };
            var result = Propose(request, authored: true); Candidate(result);
            Assert.That(result.Electrical!.Omissions.Any(omission => omission.ConsumerId == "Preference" && omission.Code ==
                (operational ? "PreferredElectricalSupplyUnverified" : "PreferredElectricalConsumerMinimumUnmet")), Is.True);
            Assert.That(result.Electrical.Consumers.Count(consumer => consumer.PrototypeId == "KsProcgenFilteredComputerFixture"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task OverlappingSelectorsDeduplicateThePhysicalConsumerAndKeepDeclaredLoads()
    {
        await AssertOnServer(() =>
        {
            var request = Request(); var baseline = Propose(request, authored: true); Candidate(baseline);
            var computer = baseline.Electrical!.Consumers.Single(item => item.PrototypeId == "KsProcgenFilteredComputerFixture");
            request.Utilities = new() { Consumers = new()
            {
                new() { Id = "All", NominalWatts = 42f, PeakWatts = 60f, Supply = KsProcgenElectricalSupply.WiredOnly },
                new() { Id = "Member", Selector = KsProcgenConsumerSelector.Member, SelectorId = computer.ConsumerId,
                    Scope = KsProcgenUtilityScope.Room, ScopeId = computer.RegionId, NominalWatts = 42f, PeakWatts = 60f,
                    Supply = KsProcgenElectricalSupply.WiredOnly },
            } };
            var result = Propose(request, authored: true); Candidate(result);
            var retained = result.Electrical!.Consumers.Single(item => item.ConsumerId == computer.ConsumerId);
            Assert.That(retained.NominalWatts, Is.EqualTo(42f)); Assert.That(retained.PeakWatts, Is.EqualTo(60f));
            Assert.That(result.Electrical.Consumers.Count, Is.EqualTo(baseline.Electrical.Consumers.Count));
            Assert.That(result.SemanticHash, Is.Not.EqualTo(baseline.SemanticHash));
        });
    }

    [Test]
    public async Task ElectricalRoutingChargesTheSharedGraphBudgetAndPublishesNoPartialPlan()
    {
        await AssertOnServer(() =>
        {
            var request = Request(); var result = Propose(request, authored: true); Candidate(result);
            var exact = Propose(request, authored: true, budgets: Budgets() with { MaximumReportedGraphExpansions = result.ExpandedCells });
            Candidate(exact); Assert.That(exact.SemanticHash, Is.EqualTo(result.SemanticHash));
            var shortResult = Propose(request, authored: true, budgets: Budgets() with { MaximumReportedGraphExpansions = result.ExpandedCells - 1 });
            Assert.That(shortResult.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.BudgetExceeded));
            Assert.That(shortResult.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.ElectricalRouting));
            Assert.That(shortResult.ExpandedCells, Is.LessThanOrEqualTo(result.ExpandedCells - 1));
            Assert.That(shortResult.Electrical, Is.Null); Assert.That(shortResult.SemanticHash, Is.Zero);
        });
    }

    [Test]
    public async Task CombinedGasNominationIncludesElectricalDeclarations()
    {
        await AssertOnServer(() =>
        {
            var request = Request(); request.AtmosphereProfile = "KsProcgenStandardAir";
            var result = Propose(request, authored: true); Candidate(result);
            Assert.That(result.Gas!.Status, Is.EqualTo(KsProcgenGasBindingStatus.Bound), result.Gas.Issue?.Code);
            Assert.That(result.Gas.ContextDeclarationHash, Is.Not.Zero);
            Assert.That(result.Gas.Domains.SelectMany(domain => domain.Cells), Is.Not.Empty);
            Assert.That(Propose(request, authored: true).SemanticHash, Is.EqualTo(result.SemanticHash));
        });
    }

    [Test]
    public async Task InfrastructureHomeRetryKeepsWorkAndDoesNotLeakRejectedRooms()
    {
        await AssertOnServer(() =>
        {
            var request = Request(); request.ElectricalProfile = null; request.Shape.AddRectangles[0].Max = new(10, 6);
            request.Shape.EnvelopeCells = Enumerable.Range(-1, 12).SelectMany(x => Enumerable.Range(-1, 8)
                .Select(y => new Vector2i(x, y))).Where(cell => cell.X is -1 or 10 || cell.Y is -1 or 6).ToList();
            var budgets = Budgets() with { PreferredMaximumRoomCells = 24 };
            var geometry = Propose(request, budgets: budgets);
            Assert.That(geometry.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), geometry.Issue?.Code);
            var homes = geometry.Themes!.Regions.Where(region => region.Kind == KsProcgenZoneKind.RoomProposal)
                .OrderByDescending(region => region.FloorCells.Count).ThenBy(region => region.Id, StringComparer.Ordinal).ToArray();
            Assert.That(homes.Length, Is.GreaterThan(1)); var target = homes.Last();
            request.ElectricalProfile = "KsProcgenWiredElectrical";
            request.Utilities = new() { DeviceWriteCells = target.FloorCells.Concat(geometry.Structure!.RemainingWalls
                .Where(wall => target.FloorCells.Any(cell => Math.Abs(cell.X - wall.Cell.X) + Math.Abs(cell.Y - wall.Cell.Y) == 1))
                .Select(wall => wall.Cell)).Distinct().ToList() };
            var result = Propose(request, budgets: budgets); Candidate(result);
            Assert.That(result.InfrastructureHomeOrdinal, Is.EqualTo(homes.Length - 1));
            Assert.That(result.Attempts.Count, Is.EqualTo(homes.Length));
            Assert.That(result.Attempts.Take(homes.Length - 1).All(attempt => attempt.Status == KsProcgenOwnedPlanStatus.Rejected), Is.True);
            Assert.That(result.Electrical!.Devices.All(device => device.RegionId == target.Id), Is.True);
            Assert.That(result.ExpandedCells, Is.EqualTo(result.Attempts.Sum(attempt => attempt.ExpandedCells)));
            Assert.That(result.FurnishingProbes, Is.EqualTo(result.Attempts.Sum(attempt => attempt.FurnishingProbes)));
            var limited = Propose(request, budgets: budgets with { MaximumLayoutProbes = homes.Length - 1 });
            Assert.That(limited.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.BudgetExceeded));
            Assert.That(limited.Electrical, Is.Null); Assert.That(limited.SemanticHash, Is.Zero);
        });
    }

    private KsProcgenOwnedInteriorResult Propose(KsProcgenRequest request, bool authored = false, KsProcgenOwnedPlanBudgets? budgets = null)
    {
        var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
        return Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, authored ? "ElectricalRoom" : "KsProcgenEmptyOfficeThemeFixture",
            Context(), [], budgets: budgets ?? Budgets(), content: authored ? Catalog(manager) : null);
    }
    private static void Candidate(KsProcgenOwnedInteriorResult result)
    {
        Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
        Assert.That(result.Electrical, Is.Not.Null); Assert.That(result.Electrical!.PlanHash, Is.Not.Zero);
        Assert.That(result.Stages.Any(stage => stage.Stage == KsProcgenOwnedPlanStage.ElectricalRouting && stage.Completed), Is.True);
    }
    internal static KsProcgenThemeCatalog Catalog(IPrototypeManager manager)
    {
        var spec = new KsProcgenContentDraftSpec
        {
            DocumentId = "ElectricalPlanning",
            Themes = new() { ["ElectricalRoom"] = new() { Parent = "KsProcgenSimpleOffice", LightingPacks = new(),
                Goals = new() { FurnishingDensity = 0f },
                EntityPacks = new() { new() { Pack = "Electrical" } } } },
            Entities = new() { ["Electrical"] = new() { Assemblies = new() { new() { Id = "Apc", Assembly = "Computer", MinimumCount = 1 } } } },
            Assemblies = new() { ["Computer"] = new() { AnchorMember = "Device", Members = new()
                { new() { Id = "Device", Entity = "KsProcgenFilteredComputerFixture", Role = KsProcgenEntityRole.Equipment,
                    RequiresInteractionApproach = true } } } },
        };
        Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code);
        Assert.That(KsProcgenThemeCatalog.TryCapture(manager, "ElectricalRoom", out var catalog, out issue, assets: draft), Is.True, issue?.Code);
        return catalog!;
    }
    private static KsProcgenOwnedPlanBudgets Budgets() => new() { PreferredMaximumRoomCells = 64, MaximumFurnishingProbes = 16384 };
    private static KsProcgenGroupRouteContext Context() => new(new HashSet<Vector2i>(), new HashSet<Vector2i>(),
        new HashSet<Vector2i>(), true, true) { ExteriorIsEmptySpace = true };
    private static KsProcgenRequest Request() => new()
    {
        SchemaVersion = 2, RequestId = "ElectricalPlanning", Seed = 17, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        ElectricalProfile = "KsProcgenWiredElectrical", EntranceDomain = new(),
        Shape = new() { AddRectangles = new() { new() { Min = new(0, 0), Max = new(6, 6) } },
                EnvelopeCells = Enumerable.Range(-1, 8).SelectMany(x => Enumerable.Range(-1, 8)
                    .Select(y => new Robust.Shared.Maths.Vector2i(x, y))).Where(cell => cell.X is -1 or 6 || cell.Y is -1 or 6).ToList() },
    };
    private static Vector2 Center(Vector2i cell) => new((float) cell.X + 0.5f, (float) cell.Y + 0.5f);
}
