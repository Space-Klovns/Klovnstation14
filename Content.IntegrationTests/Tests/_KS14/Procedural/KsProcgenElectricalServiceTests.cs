#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Server.Power.Components;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenElectricalServiceWitness))]
public sealed class KsProcgenElectricalServiceTests : KsProcgenGameTest
{
    [TestCase(2, 2, false)] [TestCase(1, 2, true)] [TestCase(2, 2, true)]
    public async Task OwnedGenerationPlacesConvertersInSeparateRoomsAndVerifiesNativeService(int width, int height, bool wall)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var result = stages.TryGenerate(Request(width, height, wall), "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(result.Stage, Is.Not.Null, result.Issue?.Code + "/" + result.Proposal?.Issue?.Code);
                var proposal = result.Proposal!; var plan = proposal.Electrical!; var service = plan.Service!;
                var closet = proposal.Themes!.Regions.Single(region => region.UtilityRoom != null);
                var substation = plan.Devices.Single(device => device.Substation); var apc = plan.Devices.Single(device => !device.Substation);
                Assert.That(substation.RegionId, Is.EqualTo(closet.Id)); Assert.That(apc.RegionId, Is.Not.EqualTo(closet.Id));
                Assert.That(substation.WallMounted, Is.EqualTo(wall)); Assert.That(apc.WallMounted, Is.True);
                Assert.That(closet.UtilityRoom!.Size.X * closet.UtilityRoom.Size.Y, Is.EqualTo(width * height));
                var clutter = proposal.Furnishings.Single(room => room.RegionId == closet.Id).Proposal.Layered!;
                Assert.That(clutter.Bindings.Any(binding => binding.Origin == KsProcgenRoomMemberOrigin.Authored && binding.PackId == "KsProcgenElectricalSupplies"), Is.True);
                Assert.That(clutter.Access.Access.Count, Is.GreaterThanOrEqualTo(1));
                Assert.That(service.Rooms.Single().RegionId, Is.EqualTo(closet.Id));
                Assert.That(service.CoveredFloorCells.Count, Is.EqualTo(service.FloorDomain.Count));
                Assert.That(service.UncoveredFloorCells, Is.Empty); Assert.That(service.ProviderRange, Is.EqualTo(3));
                Assert.That(service.MediumVoltageTargets.Count, Is.GreaterThan(plan.Devices.Count));
                Assert.That(service.MediumVoltageTargets.All(cell => plan.Cables.Any(cable => cable.Cell == cell && cable.Voltage == KsProcgenElectricalVoltage.Medium)), Is.True);
                Assert.That(Blocks(plan), Is.Empty); Assert.That(service.SquareProofs, Is.Empty);
                var initialized = stages.TryInitialize(result.Stage!);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), initialized.Issue?.Code);
                Assert.That(result.Stage!.InitialElectrical!.ElectricalTopologyVerified, Is.True);
                Assert.That(result.Stage.InitialElectrical.ElectricalSupplyVerified, Is.False);
                Assert.That(stages.Verify(result.Stage), Is.True);
                Assert.That(stages.TryInitialize(result.Stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task DefaultSchemaTwoProfileProducesACompleteOwnedCandidate()
    {
        await AssertOnServer(() =>
        {
            var request = Request(1, 2, false); request.Electrical = null; request.ElectricalProfile = "KsProcgenClosetElectrical";
            var result = Propose(request);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            Assert.That(result.Electrical!.Service, Is.Not.Null);
            Assert.That(result.Electrical.Devices.Where(device => device.Substation).All(device =>
                result.Themes!.Regions.Single(region => region.Id == device.RegionId).UtilityRoom != null), Is.True);
        });
    }

    [TestCase(0f)] [TestCase(0.5f)] [TestCase(1f)]
    public async Task FloorCoveragePreferencesCannotWaiveRequiredActualConsumers(float fraction)
    {
        await AssertOnServer(() =>
        {
            var request = Request(2, 2, false); request.Electrical!.Distribution!.FloorServiceFraction = fraction;
            request.Utilities = new() { Consumers = new() { new() { Id = "Computer", Selector = KsProcgenConsumerSelector.Prototype,
                SelectorId = "KsProcgenFilteredComputerFixture", Supply = KsProcgenElectricalSupply.WiredOnly, MinimumMatches = 1 } } };
            var catalog = KsProcgenElectricalPlanningTests.Catalog(Pair.Server.ResolveDependency<IPrototypeManager>());
            var before = SEntMan.GetEntities().ToHashSet();
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, catalog.Theme.Id, Context(), [], budgets: Budgets(), content: catalog);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var service = result.Electrical!.Service!;
            Assert.That(service.RequiredFloorCells, Is.EqualTo((int) Math.Ceiling((double) ((float) service.FloorDomain.Count * fraction))));
            Assert.That(service.CoveredFloorCells.Count, Is.GreaterThanOrEqualTo(service.RequiredFloorCells));
            Assert.That(service.CoveredFloorCells.Concat(service.UncoveredFloorCells), Is.EquivalentTo(service.FloorDomain));
            Assert.That(result.Electrical.Consumers.Count(consumer => consumer.PrototypeId == "KsProcgenFilteredComputerFixture"), Is.EqualTo(1));
            Assert.That(Blocks(result.Electrical), Is.Empty); Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task SharedRoutingBudgetShortfallCannotReturnAPartialServicePlan()
    {
        await AssertOnServer(() =>
        {
            var request = Request(2, 2, false); var complete = Propose(request);
            Assert.That(complete.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), complete.Issue?.Code);
            var exact = Propose(request, budgets: Budgets() with { MaximumReportedGraphExpansions = complete.ExpandedCells });
            Assert.That(exact.SemanticHash, Is.EqualTo(complete.SemanticHash));
            var shortfall = Propose(request, budgets: Budgets() with { MaximumReportedGraphExpansions = complete.ExpandedCells - 1 });
            Assert.That(shortfall.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.BudgetExceeded));
            Assert.That(shortfall.Electrical, Is.Null); Assert.That(shortfall.Furnishings, Is.Empty); Assert.That(shortfall.SemanticHash, Is.Zero);
        });
    }

    [Test]
    public async Task ARemovedNativeCableProviderInvalidatesServiceEvenWithoutAuthoredMachines()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var result = stages.TryGenerate(Request(2, 2, false), "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(result.Stage, Is.Not.Null, result.Issue?.Code);
                Assert.That(stages.TryInitialize(result.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                var cable = result.Proposal!.Electrical!.Cables.First(item => item.Voltage == KsProcgenElectricalVoltage.Low);
                var native = SEntMan.AllEntityQueryEnumerator<ExtensionCableProviderComponent, MetaDataComponent, TransformComponent>();
                var found = EntityUid.Invalid;
                while (native.MoveNext(out var uid, out _, out var metadata, out var transform))
                    if (transform.ParentUid == result.Stage!.GridUid && metadata.EntityPrototype?.ID == cable.PrototypeId &&
                        transform.LocalPosition == new System.Numerics.Vector2((float) cable.Cell.X + 0.5f, (float) cable.Cell.Y + 0.5f)) found = uid;
                Assert.That(found, Is.Not.EqualTo(EntityUid.Invalid));
                SEntMan.RemoveComponent<ExtensionCableProviderComponent>(found);
                Assert.That(stages.Verify(result.Stage!), Is.False);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task FrozenProjectClosetContentSurvivesLoadedPackChangesDuringNativeGeneration()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var request = Request(2, 2, false);
            request.RequestId = "Area"; request.Electrical!.Distribution!.ClosetTheme = "LocalCloset";
            Assert.That(KsProcgenProject.TryCapture(manager, new() { DocumentId = "ServiceProject",
                Content = new() { DocumentId = "ServiceProject", Themes = new() { ["LocalCloset"] = new() { Parent = "KsProcgenElectricalCloset" } } },
                Areas = new() { ["Area"] = new() { Request = request, Theme = "KsProcgenEmptyOfficeThemeFixture" } } },
                out var project, out var issue), Is.True, issue?.Code);
            var area = project!.Areas.Single(); Assert.That(area.UtilityContent!.Theme.Id, Is.EqualTo("LocalCloset"));
            var packId = "KsProcgenElectricalSupplies"; var loaded = manager.Index<KsProcgenEntityPackPrototype>(packId);
            var saved = loaded.Entries.Select(entry => entry.Entity).ToArray();
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                foreach (var entry in loaded.Entries) entry.Entity = "Paper";
                var generated = stages.TryGenerate(area, planningBudgets: Budgets());
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var closet = generated.Proposal!.Themes!.Regions.Single(region => region.UtilityRoom != null);
                Assert.That(closet.Theme.ThemeId, Is.EqualTo("LocalCloset"));
                var members = generated.Proposal.Furnishings.Single(room => room.RegionId == closet.Id).Proposal.Layered!;
                var authored = members.Bindings.Where(binding => binding.Origin == KsProcgenRoomMemberOrigin.Authored).Select(binding => binding.MemberId).ToHashSet();
                Assert.That(authored, Is.Not.Empty);
                Assert.That(members.Access.Poses!.Members.Where(pose => authored.Contains(pose.Support.MemberId))
                    .All(pose => saved.Contains(pose.Support.EntityPrototypeId)), Is.True);
                Assert.That(stages.TryInitialize(generated.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(generated.Stage!), Is.True);
                var rejected = stages.TryGenerate(area with { UtilityContent = null }, planningBudgets: Budgets());
                Assert.That(rejected.Stage, Is.Null); Assert.That(rejected.Issue?.Code, Is.EqualTo("OwnedProjectUtilityCatalogRequired"));
            }
            finally
            {
                stages.DiscardAll();
                for (var index = 0; index < saved.Length; index++) loaded.Entries[index].Entity = saved[index];
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task RequiredMediumServiceTargetsCannotBeDroppedWhenTheirCellIsNotWritable()
    {
        await AssertOnServer(() =>
        {
            var request = Request(2, 2, false); var baseline = Propose(request);
            Assert.That(baseline.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), baseline.Issue?.Code);
            var denied = baseline.Partition!.ProtectedCells.First();
            Assert.That(baseline.Electrical!.Service!.MediumVoltageTargets, Does.Contain(denied));
            request.Utilities = new() { CableWriteCells = baseline.Electrical.Service.AllowedCableCells.Where(cell => cell != denied).ToList() };
            var result = Propose(request);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Rejected), result.Issue?.Code);
            Assert.That(result.Issue?.Code, Is.EqualTo("ElectricalMediumServicePermissionRequired"));
            Assert.That(result.Electrical, Is.Null); Assert.That(result.SemanticHash, Is.Zero);
        });
    }

    [Test]
    public async Task SparseEndpointWiringCannotSatisfyFullFloorService()
    {
        await AssertOnServer(() =>
        {
            var request = Request(2, 2, false); var policy = request.Electrical!.Distribution!;
            request.Shape.AddRectangles.Single().Max = new(8, 8);
            request.Shape.EnvelopeCells = Enumerable.Range(-1, 10).SelectMany(x => Enumerable.Range(-1, 10).Select(y => new Vector2i(x, y)))
                .Where(cell => cell.X is -1 or 8 || cell.Y is -1 or 8).ToList();
            policy.MediumVoltageCirculationBackbone = false; policy.MediumVoltageRoomEntryBranches = false; policy.FloorServiceFraction = 0f;
            var sparse = Propose(request);
            Assert.That(sparse.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), sparse.Issue?.Code);
            request.Utilities = new() { CableWriteCells = sparse.Electrical!.Cables.Select(cable => cable.Cell).Distinct().ToList() };
            var endpointOnly = Propose(request);
            Assert.That(endpointOnly.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), endpointOnly.Issue?.Code);
            Assert.That(endpointOnly.Electrical!.Service!.UncoveredFloorCells, Is.Not.Empty);
            policy.FloorServiceFraction = 1f;
            var full = Propose(request, budgets: Budgets() with { MaximumLayoutProbes = 1 });
            Assert.That(full.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.BudgetExceeded), full.Issue?.Code);
            Assert.That(full.Attempts.Single().ReasonCode, Is.EqualTo("ElectricalFloorServiceUnmet"));
            // Other reservations are now eligible for retry; one tested failure is not a global proof.
            Assert.That(full.Electrical, Is.Null); Assert.That(full.Furnishings, Is.Empty); Assert.That(full.SemanticHash, Is.Zero);
        });
    }

    [TestCase("CableOmission", "OwnedElectricalCableDomainDrift")]
    [TestCase("CableSubstitution", "OwnedElectricalCableDomainDrift")]
    [TestCase("MediumOmission", "OwnedElectricalMediumTargetDomainDrift")]
    [TestCase("MediumDuplicate", "OwnedElectricalMediumTargetDomainDrift")]
    [TestCase("FloorOmission", "OwnedElectricalFloorServiceDomainDrift")]
    [TestCase("BlockedFloorOverclaim", "OwnedElectricalFloorServiceDomainDrift")]
    [TestCase("RoomOmission", "OwnedElectricalUtilityRoomDomainDrift")]
    public async Task NativeLayoutAuditRejectsIncompleteOrSubstitutedServiceClaims(string change, string expectedIssue)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var electrical = Pair.Server.System<KsProcgenOwnedElectricalSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(Request(2, 2, false), "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!; var service = stage.Electrical!.Service!;
                var allowed = service.AllowedCableCells.ToList(); var medium = service.MediumVoltageTargets.ToList();
                var floor = service.FloorDomain.ToList(); var rooms = service.Rooms.ToList();
                Assert.That(electrical.InspectServiceLayout(stage, allowed, medium, floor, rooms, () => true), Is.Null);
                switch (change)
                {
                    case "CableOmission": allowed.RemoveAt(0); break;
                    case "CableSubstitution": allowed[0] = new(100, 100); break;
                    case "MediumOmission":
                        var terminals = stage.Electrical.Devices.Select(device => device.Cell).ToHashSet();
                        medium.Remove(medium.First(cell => !terminals.Contains(cell))); break;
                    case "MediumDuplicate": medium[0] = medium[^1]; break;
                    case "FloorOmission": floor.RemoveAt(0); break;
                    case "BlockedFloorOverclaim":
                        floor.Add(stage.Scene.Blocked.First(cell => stage.Scene.Floor.Contains(cell) && !floor.Contains(cell))); break;
                    case "RoomOmission": rooms.Clear(); break;
                    default: throw new InvalidOperationException(change);
                }
                Assert.That(electrical.InspectServiceLayout(stage, allowed, medium, floor, rooms, () => true), Is.EqualTo(expectedIssue));
                Assert.That(stage.InitialElectrical, Is.Null);
                // Forged claims do not damage the original candidate or its native initialization.
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(stage), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task NativeFloorDomainAuditDistinguishesUsableAndAllRoomFloor(bool allFloor)
    {
        await AssertOnServer(() =>
        {
            var request = Request(2, 2, false);
            request.Electrical!.Distribution!.FloorServiceDomain = allFloor ? KsProcgenElectricalFloorDomain.AllRoomFloor :
                KsProcgenElectricalFloorDomain.UsableRoomFloor;
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(request, "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!; var service = stage.Electrical!.Service!;
                var roomFloor = generated.Proposal!.Themes!.Regions.Where(region => region.Kind == KsProcgenZoneKind.RoomProposal)
                    .SelectMany(region => region.FloorCells).ToHashSet();
                Assert.That(service.FloorDomain, Is.EquivalentTo(allFloor ? roomFloor : roomFloor.Except(stage.Scene.Blocked)));
                var substation = stage.Electrical.Devices.Single(device => device.Substation);
                Assert.That(service.FloorDomain.Contains(substation.Cell), Is.EqualTo(allFloor));
                var coil = stage.Scene.Assemblies.SelectMany(assembly => assembly.Poses.Members)
                    .First(member => member.Support.EntityPrototypeId.StartsWith("Cable", StringComparison.Ordinal));
                var coilCell = new Vector2i((int) Math.Floor((double) coil.Position.X), (int) Math.Floor((double) coil.Position.Y));
                Assert.That(service.FloorDomain, Does.Contain(coilCell));
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), initialized.Issue?.Code);
                Assert.That(stages.Verify(stage), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task NativeServiceDomainAuditSharesAnExactReadOnlyWorkAllowance()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var electrical = Pair.Server.System<KsProcgenOwnedElectricalSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(Request(2, 2, false), "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!; var service = stage.Electrical!.Service!;
                var used = 0; var maximum = int.MaxValue;
                bool Spend() { if (used >= maximum) return false; used++; return true; }
                string? Inspect() => electrical.InspectServiceLayout(stage, service.AllowedCableCells, service.MediumVoltageTargets,
                    service.FloorDomain, service.Rooms, Spend);
                Assert.That(Inspect(), Is.Null); var completeWork = used; Assert.That(completeWork, Is.GreaterThan(0));
                maximum = completeWork; used = 0; Assert.That(Inspect(), Is.Null); Assert.That(used, Is.EqualTo(completeWork));
                maximum = completeWork - 1; used = 0;
                Assert.That(Inspect(), Is.EqualTo("OwnedElectricalWorkBudget")); Assert.That(used, Is.EqualTo(maximum));
                Assert.That(stage.InitialElectrical, Is.Null);
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(stage), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task CapturedNativeServiceLayoutCannotAliasMutablePlannerCollections()
    {
        await AssertOnServer(() =>
        {
            var proposal = Propose(Request(2, 2, false));
            Assert.That(proposal.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), proposal.Issue?.Code);
            var sources = proposal.Themes!.Regions.Select(region => (Region: region,
                Floor: region.FloorCells.ToList(), Protected: region.ProtectedPassageCells.ToList())).ToList();
            var regions = sources.Select(source => source.Region with { FloorCells = source.Floor,
                ProtectedPassageCells = source.Protected }).ToList();
            var doors = proposal.Structure!.SourceHull!.InteriorMaterials!.DoorOpenings.ToList();
            var captured = new KsProcgenOwnedElectricalLayout(regions, doors);
            var floors = captured.Regions.ToDictionary(region => region.Id, region => region.Floor.ToArray());
            var passages = captured.Regions.ToDictionary(region => region.Id, region => region.Protected.ToArray());
            var capturedDoors = captured.Doors.ToArray();
            Assert.That(floors.Values.All(cells => cells.Length > 0), Is.True);
            Assert.That(passages.Values.Any(cells => cells.Length > 0), Is.True);
            Assert.That(capturedDoors, Is.Not.Empty);
            foreach (var source in sources) { source.Floor.Clear(); source.Protected.Clear(); }
            regions.Clear(); doors.Clear();
            Assert.That(captured.Regions.Count, Is.EqualTo(floors.Count));
            foreach (var region in captured.Regions)
            {
                Assert.That(region.Floor, Is.EqualTo(floors[region.Id]));
                Assert.That(region.Protected, Is.EqualTo(passages[region.Id]));
            }
            Assert.That(captured.Doors, Is.EqualTo(capturedDoors));
        });
    }

    [TestCase("Cell")] [TestCase("Rotation")] [TestCase("Prototype")] [TestCase("Region")]
    [TestCase("Role")] [TestCase("Mount")] [TestCase("Duplicate")] [TestCase("Omission")]
    public async Task ConverterClaimsCannotBorrowAuthorityFromAConnectedPowerBus(string change)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var electrical = Pair.Server.System<KsProcgenOwnedElectricalSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(Request(2, 2, false), "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!; var plan = stage.Electrical!;
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stage.InitialElectrical!.ElectricalTopologyVerified, Is.True);
                Assert.That(electrical.InspectPolicyAndDevices(stage, plan.Profile, plan.Devices, () => true), Is.Null);
                var devices = plan.Devices.ToList(); var index = devices.FindIndex(device => !device.Substation); var apc = devices[index];
                switch (change)
                {
                    case "Cell":
                        // Another real provider on the same LV bus cannot stand in for the APC terminal.
                        var other = plan.Cables.First(cable => cable.Voltage == KsProcgenElectricalVoltage.Low && cable.Cell != apc.Cell);
                        devices[index] = apc with { Cell = other.Cell }; break;
                    case "Rotation": devices[index] = apc with { QuarterTurns = (apc.QuarterTurns + 1) % 4 }; break;
                    case "Prototype": devices[index] = apc with { PrototypeId = "SubstationBasic" }; break;
                    case "Region": devices[index] = apc with { RegionId = "OtherRoom" }; break;
                    case "Role": devices[index] = apc with { Substation = true }; break;
                    case "Mount": devices[index] = apc with { WallMounted = false }; break;
                    case "Duplicate": devices[index] = devices.First(device => device.Substation); break;
                    case "Omission": devices.RemoveAt(index); break;
                    default: throw new InvalidOperationException(change);
                }
                Assert.That(electrical.InspectPolicyAndDevices(stage, plan.Profile, devices, () => true),
                    Is.EqualTo("OwnedElectricalConverterAuthorityDrift"));
                Assert.That(stages.Verify(stage), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("Floor")] [TestCase("Medium")] [TestCase("Palette")]
    public async Task RoutedPolicyCannotWeakenTheAuthorSelectedElectricalProfile(string change)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var electrical = Pair.Server.System<KsProcgenOwnedElectricalSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(Request(2, 2, false), "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!; var original = stage.Scene.ElectricalProfile!;
                Assert.That(original.SemanticHash, Is.EqualTo(stage.Electrical!.Profile.SemanticHash));
                var changed = original.CopySpec();
                if (change == "Floor") changed.Distribution!.FloorServiceFraction = 0f;
                else if (change == "Medium") changed.Distribution!.MediumVoltageRoomEntryBranches = false;
                else changed.Substations.Single().Weight = 2f;
                Assert.That(KsProcgenElectricalProfiles.TrySnapshot(changed, out var profile, out var issue), Is.True, issue?.Code);
                Assert.That(electrical.InspectPolicyAndDevices(stage, profile!, stage.Electrical.Devices, () => true),
                    Is.EqualTo("OwnedElectricalPolicyAuthorityDrift"));
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(stage), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task NativeConvertersMustRemainAtTheCapturedPhysicalPose(bool substation)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var electrical = Pair.Server.System<KsProcgenOwnedElectricalSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(Request(2, 2, false), "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!; var device = stage.Electrical!.Devices.Single(item => item.Substation == substation);
                var assemblyIndex = Enumerable.Range(0, stage.Scene.Assemblies.Count)
                    .Single(index => stage.Scene.Assemblies[index].RegionId == device.RegionId);
                var binding = stage.Scene.Assemblies[assemblyIndex].Bindings.Single(item => device.Id == device.RegionId + "/" + item.MemberId);
                var uid = stage.Assemblies[assemblyIndex][binding.MemberId];
                var transforms = Pair.Server.System<SharedTransformSystem>();
                var transform = SEntMan.GetComponent<TransformComponent>(uid);
                if (substation)
                {
                    var position = transform.LocalPosition;
                    transforms.Unanchor(uid); transforms.SetLocalPosition(uid, position + System.Numerics.Vector2.UnitX);
                    Assert.That(transforms.AnchorEntity((uid, transform)), Is.True);
                    Assert.That(transform.LocalPosition, Is.Not.EqualTo(position));
                }
                else
                {
                    var rotation = transform.LocalRotation;
                    transforms.SetLocalRotation(uid, KsProcgenRotation.ToEngineAngle((device.QuarterTurns + 1) % 4));
                    Assert.That(transform.LocalRotation, Is.Not.EqualTo(rotation));
                }
                Assert.That(transform.Anchored, Is.True);
                Assert.That(electrical.InspectPolicyAndDevices(stage, stage.Electrical.Profile, stage.Electrical.Devices, () => true),
                    Is.EqualTo("OwnedElectricalConverterPhysicalDrift"));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ConverterAuthorityAuditHonorsAnExactSharedWorkBudget()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var electrical = Pair.Server.System<KsProcgenOwnedElectricalSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(Request(2, 2, false), "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!; var used = 0; var maximum = int.MaxValue;
                bool Spend() { if (used >= maximum) return false; used++; return true; }
                string? Inspect() => electrical.InspectPolicyAndDevices(stage, stage.Electrical!.Profile, stage.Electrical.Devices, Spend);
                Assert.That(Inspect(), Is.Null); var complete = used; maximum = complete; used = 0;
                Assert.That(Inspect(), Is.Null); Assert.That(used, Is.EqualTo(complete));
                maximum--; used = 0; Assert.That(Inspect(), Is.EqualTo("OwnedElectricalWorkBudget"));
                Assert.That(used, Is.EqualTo(maximum)); Assert.That(stage.InitialElectrical, Is.Null);
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task SceneRetainsEffectiveThemePolicyAndExplicitRequestPrecedence(bool explicitRequest)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var spec = new KsProcgenContentDraftSpec { DocumentId = "ElectricalAuthority",
                Themes = new() { ["Local"] = new() { Parent = "KsProcgenEmptyOfficeThemeFixture", ElectricalProfile = "KsProcgenClosetElectrical" } } };
            Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code);
            Assert.That(KsProcgenThemeCatalog.TryCapture(manager, "Local", out var catalog, out issue, assets: draft), Is.True, issue?.Code);
            var request = Request(2, 2, false);
            if (explicitRequest) request.Electrical!.Distribution!.FloorServiceFraction = 0.5f;
            else request.Electrical = null;
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(request, "Local", planningBudgets: Budgets(), content: catalog);
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!; var original = stage.Scene.ElectricalProfile!;
                Assert.That(stage.Request.Electrical == null, Is.EqualTo(!explicitRequest));
                Assert.That(original.SemanticHash, Is.EqualTo(stage.Electrical!.Profile.SemanticHash));
                Assert.That(original.Distribution!.FloorServiceFraction, Is.EqualTo(explicitRequest ? 0.5f : 1f));
                spec.Themes["Local"].ElectricalProfile = "KsProcgenWiredElectrical";
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(stage), Is.True);
                Assert.That(original.Distribution.FloorServiceFraction, Is.EqualTo(explicitRequest ? 0.5f : 1f));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task AutomaticClosetSelectionIncludesCoherentSuppliesWithoutASizeOverride()
    {
        await AssertOnServer(() =>
        {
            var request = Request(1, 2, false); request.Electrical = null; request.ElectricalProfile = "KsProcgenClosetElectrical";
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(request, "KsProcgenEmptyOfficeThemeFixture");
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var proposal = generated.Proposal!; var closet = proposal.Themes!.Regions.Single(region => region.UtilityRoom != null);
                Assert.That(closet.UtilityRoom!.Size, Is.EqualTo(new Vector2i(2, 2)));
                Assert.That(proposal.Furnishings.Single(region => region.RegionId == closet.Id).Proposal.Layered!.Bindings
                    .Any(binding => binding.Origin == KsProcgenRoomMemberOrigin.Authored && binding.PackId == "KsProcgenElectricalSupplies"), Is.True);
                Assert.That(proposal.UtilityRoomChoices.Single().Ordinal, Is.Zero);
                Assert.That(stages.TryInitialize(generated.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(generated.Stage!), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task RejectedConverterPlacementRetriesACompleteUtilityReservation()
    {
        await AssertOnServer(() =>
        {
            var request = Request(2, 2, false); var baseline = Propose(request);
            Assert.That(baseline.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), baseline.Issue?.Code);
            var denied = baseline.Themes!.Regions.Single(region => region.UtilityRoom != null).UtilityRoom!.InteriorCells.ToHashSet();
            request.Utilities = new() { DeviceWriteCells = baseline.Electrical!.Service!.AllowedCableCells.Where(cell => !denied.Contains(cell)).ToList() };
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(request, "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var proposal = generated.Proposal!;
                Assert.That(proposal.UtilityRoomChoices.Single().Ordinal, Is.GreaterThan(0));
                Assert.That(proposal.Attempts.First().Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Rejected));
                Assert.That(proposal.Attempts.First().UtilityRoomChoices.Single().Ordinal, Is.Zero);
                Assert.That(denied.Contains(proposal.Electrical!.Devices.Single(device => device.Substation).Cell), Is.False);
                Assert.That(stages.TryInitialize(generated.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(generated.Stage!), Is.True);
                Assert.That(Propose(request).SemanticHash, Is.EqualTo(proposal.SemanticHash));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ReservationRetriesShareHardBudgetsAndNeverPublishAnEarlierPartialPlan()
    {
        await AssertOnServer(() =>
        {
            var request = Request(2, 2, false); var baseline = Propose(request);
            var denied = baseline.Themes!.Regions.Single(region => region.UtilityRoom != null).UtilityRoom!.InteriorCells.ToHashSet();
            request.Utilities = new() { DeviceWriteCells = baseline.Electrical!.Service!.AllowedCableCells.Where(cell => !denied.Contains(cell)).ToList() };
            var before = SEntMan.GetEntities().ToHashSet();
            var limited = Propose(request, budgets: Budgets() with { MaximumLayoutProbes = 1 });
            Assert.That(limited.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.BudgetExceeded));
            Assert.That(limited.Attempts.Count, Is.EqualTo(1)); Assert.That(limited.Attempts.Single().Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Rejected));
            Assert.That(limited.SemanticHash, Is.Zero); Assert.That(limited.Furnishings, Is.Empty); Assert.That(limited.Electrical, Is.Null);
            var complete = Propose(request); Assert.That(complete.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), complete.Issue?.Code);
            var exact = Propose(request, budgets: Budgets() with { MaximumReportedGraphExpansions = complete.ExpandedCells,
                MaximumPartitionSeamProbes = complete.PartitionSeamProbes });
            Assert.That(exact.SemanticHash, Is.EqualTo(complete.SemanticHash));
            var shortfall = Propose(request, budgets: Budgets() with { MaximumReportedGraphExpansions = complete.ExpandedCells - 1 });
            Assert.That(shortfall.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.BudgetExceeded));
            Assert.That(shortfall.SemanticHash, Is.Zero); Assert.That(shortfall.Electrical, Is.Null); Assert.That(shortfall.Furnishings, Is.Empty);
            Assert.That(shortfall.ExpandedCells, Is.LessThanOrEqualTo(complete.ExpandedCells - 1));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task IndependentNetworksRetryOnlyTheReservationThatNeedsToMove()
    {
        await AssertOnServer(() =>
        {
            var request = Request(1, 2, false);
            request.ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland;
            request.Shape.AddRectangles.Add(new() { Min = new(10, 0), Max = new(16, 6) });
            request.Shape.EnvelopeCells.AddRange(Enumerable.Range(9, 8).SelectMany(x => Enumerable.Range(-1, 8).Select(y => new Vector2i(x, y)))
                .Where(cell => cell.X is 9 or 16 || cell.Y is -1 or 6));
            var budgets = Budgets() with { MaximumReportedGraphExpansions = 1_000_000, MaximumPartitionSeamProbes = 1_000_000,
                MaximumFurnishingProbes = 65_536 };
            var baseline = Propose(request, budgets: budgets);
            Assert.That(baseline.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), baseline.Issue?.Code);
            var closets = baseline.Themes!.Regions.Where(region => region.UtilityRoom != null).OrderBy(region => region.Id, StringComparer.Ordinal).ToArray();
            Assert.That(closets.Length, Is.EqualTo(2)); var denied = closets[1].UtilityRoom!.InteriorCells.ToHashSet();
            request.Utilities = new() { DeviceWriteCells = baseline.Electrical!.Service!.AllowedCableCells.Where(cell => !denied.Contains(cell)).ToList() };
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(request, "KsProcgenEmptyOfficeThemeFixture", planningBudgets: budgets);
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var choices = generated.Proposal!.UtilityRoomChoices.OrderBy(choice => choice.NetworkId, StringComparer.Ordinal).ToArray();
                Assert.That(choices[0].Ordinal, Is.Zero); Assert.That(choices[1].Ordinal, Is.GreaterThan(0));
                var retained = generated.Proposal.Themes!.Regions.Single(region => region.Id == closets[0].Id).UtilityRoom!;
                Assert.That(retained.Minimum, Is.EqualTo(closets[0].UtilityRoom!.Minimum));
                Assert.That(retained.Threshold, Is.EqualTo(closets[0].UtilityRoom!.Threshold));
                Assert.That(stages.TryInitialize(generated.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(generated.Stage!), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(1, 4, true, 0f)] [TestCase(4, 1, true, 0f)]
    [TestCase(4, 4, false, 0f)] [TestCase(6, 6, true, 0.5f)]
    public async Task HullSharingSupportsSmallNativeElectricalRoomsAndCompleteCoverage(int width, int height, bool wall, float windows)
    {
        await AssertOnServer(() =>
        {
            var request = Request(wall ? 1 : 2, 2, wall);
            request.Shape.AddRectangles.Single().Max = new(width, height);
            request.Shape.EnvelopeCells = Enumerable.Range(-1, width + 2).SelectMany(x => Enumerable.Range(-1, height + 2)
                .Select(y => new Vector2i(x, y))).Where(cell => cell.X == -1 || cell.X == width || cell.Y == -1 || cell.Y == height).ToList();
            request.WindowGoal.ExteriorWindowFraction = windows;
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(request, "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code + "/" + generated.Proposal?.Issue?.Code);
                var proposal = generated.Proposal!; var room = proposal.Themes!.Regions.Single(region => region.UtilityRoom != null).UtilityRoom!;
                Assert.That(room.ReusedHullCells, Is.Not.Empty);
                var structural = proposal.Structure!.RemainingWalls.Select(item => item.Cell).Concat(proposal.Structure.Windows.Select(item => item.Cell)).ToHashSet();
                Assert.That(structural, Is.SupersetOf(room.EnclosureCells.Where(cell => cell != room.Threshold)));
                var service = proposal.Electrical!.Service!;
                Assert.That(service.UncoveredFloorCells, Is.Empty); Assert.That(Blocks(proposal.Electrical), Is.Empty);
                Assert.That(service.CoveredFloorCells, Is.EquivalentTo(service.FloorDomain));
                Assert.That(service.MediumVoltageTargets.Count, Is.GreaterThan(proposal.Electrical.Devices.Count));
                var substation = proposal.Electrical.Devices.Single(device => device.Substation);
                Assert.That(substation.WallMounted, Is.EqualTo(wall));
                if (wall) Assert.That(room.ReusedHullCells, Does.Contain(substation.Cell));
                if (windows > 0) Assert.That(proposal.Structure.Windows, Is.Not.Empty);
                Assert.That(stages.TryInitialize(generated.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(generated.Stage!.InitialElectrical!.ElectricalTopologyVerified, Is.True);
                Assert.That(stages.Verify(generated.Stage), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task RemovingAReusedNativeEnclosureCornerInvalidatesTheWholeStage()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = stages.TryGenerate(Request(2, 2, false), "KsProcgenEmptyOfficeThemeFixture", planningBudgets: Budgets());
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!; var proposal = generated.Proposal!;
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                var room = proposal.Themes!.Regions.Single(region => region.UtilityRoom != null).UtilityRoom!;
                var corner = room.ReusedHullCells.First(cell => !proposal.Structure!.SourceHull!.Boundary!.Edges.Any(edge => edge.BoundaryCell == cell));
                var index = Enumerable.Range(0, stage.Scene.Entities.Count).Single(candidate =>
                    stage.Scene.Entities[candidate].Kind == KsProcgenOwnedSceneEntityKind.Wall &&
                    stage.Scene.Entities[candidate].Position == new System.Numerics.Vector2(corner.X + 0.5f, corner.Y + 0.5f));
                SEntMan.DeleteEntity(stage.Entities[index]);
                Assert.That(stages.Verify(stage), Is.False);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenOwnedInteriorResult Propose(KsProcgenRequest request, KsProcgenOwnedPlanBudgets? budgets = null) =>
        Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "KsProcgenEmptyOfficeThemeFixture", Context(), [], budgets: budgets ?? Budgets());
    private static KsProcgenGroupRouteContext Context() => new(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true) { ExteriorIsEmptySpace = true };
    private static KsProcgenOwnedPlanBudgets Budgets() => new() { PreferredMaximumRoomCells = 64, MaximumFurnishingProbes = 32768 };
    private static KsProcgenRequest Request(int width, int height, bool wall) => new()
    {
        SchemaVersion = 2, RequestId = "ElectricalService", Seed = 17, EntranceDomain = new(),
        WindowGoal = new() { ExteriorWindowFraction = 0f },
        Shape = new() { AddRectangles = new() { new() { Min = new(0, 0), Max = new(6, 6) } },
            EnvelopeCells = Enumerable.Range(-1, 8).SelectMany(x => Enumerable.Range(-1, 8).Select(y => new Vector2i(x, y)))
                .Where(cell => cell.X is -1 or 6 || cell.Y is -1 or 6).ToList() },
        Electrical = new() { SchemaVersion = 2, Mode = KsProcgenElectricalMode.AutoDistribute, SupplyRequirement = KsProcgenElectricalSupply.WiredOnly,
            Substations = new() { new() { Prototype = wall ? "SubstationWallBasic" : "SubstationBasic" } },
            Distribution = new() { ClosetInteriorSizes = new() { new(width, height) } } },
    };
    private static IReadOnlyList<Vector2i> Blocks(KsProcgenElectricalPlan plan)
    {
        var cells = plan.Cables.Where(cable => cable.Voltage == KsProcgenElectricalVoltage.Low).Select(cable => cable.Cell).ToHashSet();
        return cells.Where(cell => cells.Contains(cell + new Vector2i(1, 0)) && cells.Contains(cell + new Vector2i(0, 1)) && cells.Contains(cell + Vector2i.One)).ToArray();
    }
}
