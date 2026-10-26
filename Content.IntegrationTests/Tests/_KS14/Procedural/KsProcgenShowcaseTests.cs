#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server._KS14.Procedural;
using Content.Server.Power.Components;
using Content.Shared._KS14.Procedural;
using Content.Shared.Power;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

/// <summary>Reproducible small examples, including native save/load checks. Export is opt-in.</summary>
[TestOf(typeof(KsProcgenPlanningSystem))]
public sealed class KsProcgenShowcaseTests : KsProcgenGameTest
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [TestCase("tiny-strip")] [TestCase("l-shape")] [TestCase("kitchenette")]
    [TestCase("gas-room")] [TestCase("wired-office")]
    public async Task SmallShowcaseComesFromTheRealPlannerAndNativeMapsRoundtrip(string example)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var maps = Pair.Server.System<SharedMapSystem>();
            var loader = Pair.Server.System<MapLoaderSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var request = Request(example); var wired = example == "wired-office";
            var themeId = example == "tiny-strip" ? "KsProcgenNoDoorThemeFixture" : "KsProcgenEmptyOfficeThemeFixture";
            KsProcgenThemeCatalog? catalog = null; KsProcgenContentDraftSpec? content = null;
            if (example is "kitchenette" or "wired-office")
            {
                content = Content(wired);
                Assert.That(KsProcgenContentDraft.TrySnapshot(content, out var draft, out var contentIssue), Is.True, contentIssue?.Code);
                themeId = "Showcase";
                Assert.That(KsProcgenThemeCatalog.TryCapture(manager, themeId, out catalog, out contentIssue, assets: draft), Is.True, contentIssue?.Code);
            }
            var budgets = new KsProcgenOwnedPlanBudgets { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64,
                MaximumFurnishingProbes = 16384 };
            var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true)
                { ExteriorIsEmptySpace = true };
            var plan = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, themeId, context, [], budgets: budgets, content: catalog);
            Assert.That(plan.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), plan.Issue?.Code);
            string? yaml = null; KsProcgenOwnedStage? stage = null; Entity<MapComponent>? reloadedMap = null;
            var state = "PlanOnly"; var nativeRoundtrip = false;
            try
            {
                // Keep native save/load temporaries scoped before exporting diagnostics.
                {
                    var generated = stages.TryGenerate(request, themeId, planningBudgets: budgets, content: catalog);
                    Assert.That(generated.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), generated.Issue?.Code);
                    stage = generated.Stage!;
                    Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                    Assert.That(stages.Verify(stage), Is.True);
                    if (example == "gas-room")
                    {
                        Assert.That(stage.InitialAtmosphere!.InitialGasVerified, Is.True);
                        Assert.That(plan.Structure!.Windows.Count, Is.GreaterThan(0));
                    }
                    state = "InitializedPausedPreview";
                    using var writer = new StringWriter(CultureInfo.InvariantCulture);
                    Assert.That(loader.TrySaveGrid(stage.GridUid, writer, options: new SerializationOptions { ExpectPreInit = false }), Is.True);
                    yaml = writer.ToString();
                    using var reader = new StringReader(yaml);
                    Assert.That(loader.TryLoadGrid(reader, example + ".yml", out reloadedMap, out var reloadedGrid,
                        options: new DeserializationOptions { PauseMaps = true, InitializeMaps = false }), Is.True);
                    Assert.That(maps.GetFilledTileCount((reloadedGrid!.Value.Owner, reloadedGrid.Value.Comp)),
                        Is.EqualTo(maps.GetFilledTileCount((stage.GridUid, SEntMan.GetComponent<MapGridComponent>(stage.GridUid)))));
                    Assert.That(maps.IsPaused(reloadedMap!.Value.Comp.MapId), Is.True);
                    var originalPrototypes = SEntMan.GetEntities().Where(uid => SEntMan.TryGetComponent<TransformComponent>(uid, out var transform) &&
                        transform.MapUid == stage.MapUid).Select(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID)
                        .Where(id => id != null).GroupBy(id => id).ToDictionary(group => group.Key!, group => group.Count());
                    var reloadedPrototypes = SEntMan.GetEntities().Where(uid => SEntMan.TryGetComponent<TransformComponent>(uid, out var transform) &&
                        transform.MapUid == reloadedMap.Value.Owner).Select(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID)
                        .Where(id => id != null).GroupBy(id => id).ToDictionary(group => group.Key!, group => group.Count());
                    Assert.That(reloadedPrototypes, Is.EquivalentTo(originalPrototypes));
                    if (wired)
                    {
                        AssertInstalledCables(stage.GridUid, plan.Electrical!);
                        AssertInstalledCables(reloadedGrid.Value.Owner, plan.Electrical!);
                    }
                    nativeRoundtrip = true;
                }
                if (wired)
                {
                    Assert.That(stage.InitialElectrical!.ElectricalTopologyVerified, Is.True);
                    Assert.That(stage.InitialElectrical.Connections.Count, Is.EqualTo(plan.Electrical!.Consumers.Count));
                    var computer = plan.Electrical.Consumers.Single(consumer => consumer.PrototypeId == "KsProcgenFilteredComputerFixture");
                    Assert.That(stage.InitialElectrical.Connections.Any(connection => connection.ConsumerId == computer.ConsumerId), Is.True);
                    Assert.That(plan.Electrical!.Devices.Single(device => !device.Substation).WallMounted, Is.True);
                    var closet = plan.Themes!.Regions.Single(room => room.UtilityRoom != null);
                    Assert.That(plan.Electrical.Devices.Single(device => device.Substation).RegionId, Is.EqualTo(closet.Id));
                    Assert.That(plan.Electrical.Devices.Single(device => !device.Substation).RegionId, Is.Not.EqualTo(closet.Id));
                    Assert.That(plan.Electrical.Service!.UncoveredFloorCells, Is.Empty);
                    Assert.That(plan.Electrical.Service.MediumVoltageTargets.Count, Is.GreaterThan(plan.Electrical.Devices.Count));
                    Assert.That(plan.Electrical.Service.SquareProofs, Is.Empty);
                    Assert.That(closet.UtilityRoom!.Size, Is.EqualTo(new Vector2i(2, 2)));
                    Assert.That(plan.Furnishings.Single(room => room.RegionId == closet.Id).Proposal.Layered!.Bindings
                        .Any(binding => binding.Origin == KsProcgenRoomMemberOrigin.Authored && binding.PackId == "KsProcgenElectricalSupplies"), Is.True);
                    Assert.That(stage.InitialElectrical.ElectricalSupplyVerified, Is.False);
                }
                var entities = new List<ShowcaseEntity>();
                foreach (var room in plan.Furnishings)
                {
                    if (room.Proposal.Layered is { } layered)
                    {
                        var bindings = layered.Bindings.ToDictionary(binding => binding.MemberId, StringComparer.Ordinal);
                        foreach (var pose in layered.Access.Poses!.Members)
                            entities.Add(new(pose.Support.EntityPrototypeId, pose.Position.X, pose.Position.Y, pose.QuarterTurns,
                                pose.Support.Layer.ToString(), room.RegionId + "/" + pose.Support.MemberId,
                                bindings[pose.Support.MemberId].SourceMemberId, pose.Support.Exposed));
                    }
                    else foreach (var entity in room.Proposal.Entities)
                        entities.Add(new(entity.EntityId, (float) entity.Cell.X + 0.5f, (float) entity.Cell.Y + 0.5f,
                            entity.QuarterTurns, "Floor", room.RegionId + "/" + entity.EntryId, entity.EntryId, true));
                }
                manager.Index(new EntProtoId("CableApcExtension")).TryComp<ExtensionCableProviderComponent>(out var provider,
                    Pair.Server.ResolveDependency<IComponentFactory>());
                var payload = new
                {
                    SchemaVersion = 1, Kind = "KsProcgenShowcase", Id = example, request.Seed, ThemeId = themeId, State = state,
                    NativeGridLoadVerified = nativeRoundtrip, Published = false, EngineAccessVerified = false,
                    WorkingLightingVerified = false, ElectricalSupplyVerified = false,
                    ElectricalTopologyVerified = stage?.InitialElectrical?.ElectricalTopologyVerified ?? false,
                    PlanHash = plan.SemanticHash.ToString(CultureInfo.InvariantCulture),
                    plan.ContentDeclarationHash, Request = new { request.SchemaVersion, request.RequestId, request.Seed,
                        Shape = request.Shape.Cells.Select(Cell), Envelope = request.Shape.EnvelopeCells.Select(Cell),
                        request.AtmosphereProfile, request.ElectricalProfile },
                    Rooms = plan.Themes!.Regions.Select(room => new { room.Id, Kind = room.Kind.ToString(), Cells = room.FloorCells.Select(Cell) }),
                    Floors = plan.Structure!.SourceHull!.InteriorMaterials!.Tiles.Select(tile => new { Cell = Cell(tile.Cell), tile.TileId }),
                    Walls = plan.Structure.RemainingWalls.Select(wall => new { Cell = Cell(wall.Cell), wall.EntityId }),
                    Windows = plan.Structure.Windows.Select(window => new { Cell = Cell(window.Cell), window.EntityId }),
                    Doors = plan.Structure.SourceHull.InteriorMaterials.Doors.Select(door => new { Cell = Cell(door.Cell), door.EntityId,
                        QuarterTurns = plan.Structure.SourceHull.InteriorMaterials.DoorOpenings.Single(opening => opening.Threshold == door.Cell).InsideApproach.X == door.Cell.X ? 0 : 1 }),
                    Entities = entities,
                    Approaches = plan.Furnishings.SelectMany(room => room.Proposal.Layered?.Access.Access ?? [])
                        .Select(access => new { access.MemberId, Cell = Cell(access.Approach), Path = access.CleanPath.Cells.Select(Cell) }),
                    Lights = plan.Lighting!.Regions.SelectMany(room => room.Proposal.Lights).Select(light => new { Cell = Cell(light.Cell), light.EntityId }),
                    Cables = plan.Electrical?.Cables.Select(cable => new { Cell = Cell(cable.Cell), cable.PrototypeId, Voltage = cable.Voltage.ToString() }),
                    TransferRadius = wired ? provider!.TransferRange : (int?) null,
                    ElectricalDevices = plan.Electrical?.Devices.Select(device => new { device.Id, device.RegionId, device.PrototypeId,
                        Cell = Cell(device.Cell), device.QuarterTurns, device.Substation, device.WallMounted }),
                    ElectricalService = plan.Electrical?.Service is { } service ? new
                    {
                        MediumTargets = service.MediumVoltageTargets.Select(Cell), FloorDomain = service.FloorDomain.Select(Cell),
                        Covered = service.CoveredFloorCells.Select(Cell), Uncovered = service.UncoveredFloorCells.Select(Cell),
                        service.RequiredFloorCells, service.ProviderRange,
                        Rooms = service.Rooms.Select(room => new { room.RegionId, room.Reservation.ThemeId,
                            Size = Cell(room.Reservation.Size), Interior = room.Reservation.InteriorCells.Select(Cell),
                            Enclosure = room.Reservation.EnclosureCells.Select(Cell), Threshold = Cell(room.Reservation.Threshold) }),
                        SquareProofs = service.SquareProofs.Select(proof => new { Minimum = Cell(proof.Minimum),
                            ForcedTerminals = proof.MandatoryTerminals.Select(Cell),
                            DomainHash = proof.CandidateDomainHash.ToString(CultureInfo.InvariantCulture), TerminalsHash = proof.TerminalHash.ToString(CultureInfo.InvariantCulture) }),
                    } : null,
                    NativeConnections = stage?.InitialElectrical?.Connections.Select(connection => new { connection.ConsumerId,
                        ProviderPrototype = SEntMan.GetComponent<MetaDataComponent>(connection.Provider).EntityPrototype!.ID,
                        connection.ActualNominalWatts, connection.ProviderTransferRange, connection.ReceiverReceptionRange }),
                    Consumers = plan.Electrical?.Consumers.Select(consumer => new { consumer.ConsumerId, consumer.PrototypeId,
                        Position = new { consumer.Position.X, consumer.Position.Y }, ProviderCell = Cell(consumer.ProviderCell),
                        consumer.NominalWatts, consumer.PeakWatts }), ElectricalOmissions = plan.Electrical?.Omissions,
                    InitialAtmosphere = stage?.InitialAtmosphere?.Cells.Where(cell => cell.OwnedMixture).Select(cell => new { Cell = Cell(cell.Cell), cell.PressureKPa,
                        cell.TemperatureKelvin, cell.Gases }),
                    plan.ExpandedCells, plan.FurnishingProbes, Fallbacks = plan.Fallbacks,
                    Notes = wired ? "Substation in an automatic electrical closet, separate wall APC, MV service trunks and full usable-floor LV coverage without 2x2 cable blocks. Initial native topology/receiver links and save/load counts verified; operating supply and post-load topology remain uncertified." :
                        "Native saved grid reloaded on a paused map. Operational use, publication and stable simulation remain uncertified.",
                };
                var json = JsonSerializer.Serialize(payload, options: Json);
                using var parsed = JsonDocument.Parse(json);
                Assert.That(parsed.RootElement.GetProperty("floors").GetArrayLength(), Is.GreaterThan(0));
                var output = Environment.GetEnvironmentVariable("KS_PROCGEN_SHOWCASE_DIR");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    var path = Path.GetFullPath(output); var current = new DirectoryInfo(Directory.GetCurrentDirectory());
                    while (current != null && !File.Exists(Path.Combine(current.FullName, "CONTRIBUTING.md"))) current = current.Parent;
                    Assert.That(current, Is.Not.Null, "Showcase export must run inside the repository.");
                    Assert.That(path.StartsWith(current!.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
                    Directory.CreateDirectory(path);
                    File.WriteAllText(Path.Combine(path, example + ".json"), json, new UTF8Encoding(false));
                    if (yaml != null) File.WriteAllText(Path.Combine(path, example + ".yml"), yaml, new UTF8Encoding(false));
                    var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
                    File.WriteAllText(Path.Combine(path, example + ".request.yml"), serialization.WriteValue(request, alwaysWrite: true, notNullableOverride: true).ToString(), new UTF8Encoding(false));
                    if (content != null) File.WriteAllText(Path.Combine(path, example + ".content.yml"),
                        serialization.WriteValue(content, alwaysWrite: true, notNullableOverride: true).ToString(), new UTF8Encoding(false));
                }
            }
            finally
            {
                if (reloadedMap != null && SEntMan.EntityExists(reloadedMap.Value.Owner)) SEntMan.DeleteEntity(reloadedMap.Value.Owner);
                stages.DiscardAll();
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private sealed record ShowcaseEntity(string PrototypeId, float X, float Y, int QuarterTurns,
        string Layer, string Id, string Role, bool Exposed);
    private static object Cell(Vector2i cell) => new { cell.X, cell.Y };
    private void AssertInstalledCables(EntityUid gridUid, KsProcgenElectricalPlan electrical)
    {
        var maps = Pair.Server.System<SharedMapSystem>(); var grid = SEntMan.GetComponent<MapGridComponent>(gridUid);
        var actual = new Dictionary<(Vector2i Cell, CableType Voltage), EntityUid>();
        var cables = SEntMan.AllEntityQueryEnumerator<CableComponent, TransformComponent>();
        while (cables.MoveNext(out var uid, out var cable, out var transform))
        {
            if (transform.ParentUid != gridUid) continue;
            var cell = new Vector2i((int) MathF.Floor(transform.LocalPosition.X), (int) MathF.Floor(transform.LocalPosition.Y));
            Assert.That(transform.Anchored, Is.True, "Cable must stay installed after native save/load: " + cell);
            var anchored = new List<EntityUid>(); maps.GetAnchoredEntities((gridUid, grid), cell, anchored);
            Assert.That(anchored, Does.Contain(uid), "Installed cable must belong to the grid's anchor lattice: " + cell);
            Assert.That(actual.TryAdd((cell, cable.CableType), uid), Is.True, "Duplicate installed cable tier: " + cell);
        }
        Assert.That(actual.Count, Is.EqualTo(electrical.Cables.Count), "Saving/loading must retain every installed cable, rather than replacing it with loose coils.");
        foreach (var cable in electrical.Cables)
        {
            var voltage = cable.Voltage == KsProcgenElectricalVoltage.High ? CableType.HighVoltage :
                cable.Voltage == KsProcgenElectricalVoltage.Medium ? CableType.MediumVoltage : CableType.Apc;
            Assert.That(actual.ContainsKey((cable.Cell, voltage)), Is.True, "Missing installed cable: " + cable.Cell);
        }
    }

    private static KsProcgenRequest Request(string example)
    {
        var request = new KsProcgenRequest { SchemaVersion = 2, RequestId = "Showcase/" + example, Seed = 17,
            ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland, EntranceDomain = new() };
        request.Shape.Cells = example == "tiny-strip" ? Enumerable.Range(0, 3).Select(x => new Vector2i(x, 0)).ToList() :
            Enumerable.Range(0, 6).SelectMany(x => Enumerable.Range(0, 6).Select(y => new Vector2i(x, y)))
                .Where(cell => example != "l-shape" || cell.X < 3 || cell.Y < 3).ToList();
        if (example != "tiny-strip")
        {
            var target = request.Shape.Cells.ToHashSet();
            request.Shape.EnvelopeCells = Enumerable.Range(-1, 8).SelectMany(x => Enumerable.Range(-1, 8).Select(y => new Vector2i(x, y)))
                .Where(cell => !target.Contains(cell) && target.Any(tile => Math.Abs(tile.X - cell.X) <= 1 && Math.Abs(tile.Y - cell.Y) <= 1)).ToList();
        }
        if (example == "gas-room")
        {
            request.AtmosphereProfile = "KsProcgenStandardAir";
            request.WindowGoal.ExteriorWindowFraction = 0.25f;
        }
        if (example == "wired-office") request.ElectricalProfile = "KsProcgenClosetElectrical";
        return request;
    }
    private static KsProcgenContentDraftSpec Content(bool wired) => new()
    {
        DocumentId = "SmallShowcase",
        Themes = new() { ["Showcase"] = new() { Parent = "KsProcgenSimpleOffice", LightingPacks = new(),
            Goals = new() { FurnishingDensity = 0f },
            EntityPacks = new() { new() { Pack = "ShowcaseContents" } } } },
        Entities = new() { ["ShowcaseContents"] = new() { Assemblies = new() { new()
            { Id = "Activity", Assembly = wired ? "Computer" : "Kitchen", MinimumCount = 1 } } } },
        Assemblies = wired ? new() { ["Computer"] = new() { AnchorMember = "Computer", Members = new()
            { new() { Id = "Computer", Entity = "KsProcgenFilteredComputerFixture", Role = KsProcgenEntityRole.Equipment,
                RequiresInteractionApproach = true } } } } : new()
        {
            ["Kitchen"] = new() { AnchorMember = "Table", Members = new()
            {
                new() { Id = "Table", Entity = "Table", Movement = KsProcgenMovementClass.VaultRequired },
                new() { Id = "Microwave", Entity = "KitchenMicrowave", Role = KsProcgenEntityRole.Equipment, RequiresInteractionApproach = true },
                new() { Id = "StoredPaper", Entity = "Paper", Role = KsProcgenEntityRole.Consumable, Movement = KsProcgenMovementClass.Clear },
            }, Relations = new()
            {
                new() { Id = "OnTable", Subject = "Microwave", Target = "Table", Kind = KsProcgenRelationKind.OnSurface },
                new() { Id = "InMicrowave", Subject = "StoredPaper", Target = "Microwave", Kind = KsProcgenRelationKind.InContainer,
                    ContainerId = "microwave_entity_container" },
            } },
        },
    };
}
