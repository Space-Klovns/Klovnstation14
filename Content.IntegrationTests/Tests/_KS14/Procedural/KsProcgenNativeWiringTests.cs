#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Content.Server._KS14.Procedural;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Shared.NodeContainer;
using Content.Shared._KS14.Procedural;
using Content.Shared.Power.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenOwnedElectricalSystem))]
public sealed class KsProcgenNativeWiringTests : KsProcgenGameTest
{
    [TestCase(false)] [TestCase(true)]
    public async Task PrimaryOwnedStageBuildsNativeWiringAndRepeatsOnlyInspect(bool computer)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet(); var generated = Generate(computer);
            var hook = Pair.Server.System<KsProcgenInsertionTestSystem>();
            try
            {
                Assert.That(generated.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), generated.Issue?.Code);
                var stage = generated.Stage!; Assert.That(stage.InitialElectrical, Is.Null); Assert.That(stages.Verify(stage), Is.True);
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), initialized.Issue?.Code);
                var receipt = stage.InitialElectrical!;
                Assert.That(receipt, Is.Not.Null); Assert.That(receipt.ElectricalTopologyVerified, Is.True);
                Assert.That(receipt.ElectricalSupplyVerified || stage.Published, Is.False);
                Assert.That(receipt.CableCount, Is.EqualTo(stage.Entities.Count(uid => SEntMan.HasComponent<CableComponent>(uid))));
                Assert.That(receipt.DeviceCount, Is.EqualTo(2)); Assert.That(receipt.Work, Is.GreaterThan(0));
                if (computer)
                {
                    Assert.That(receipt.Connections.Count, Is.EqualTo(1)); var connection = receipt.Connections.Single();
                    var receiver = SEntMan.GetComponent<ExtensionCableReceiverComponent>(connection.Consumer);
                    Assert.That(receiver.Provider!.Value.Owner, Is.EqualTo(connection.Provider));
                    Assert.That(SEntMan.GetComponent<ApcPowerReceiverComponent>(connection.Consumer).Provider,
                        Is.SameAs(SEntMan.GetComponent<ApcPowerProviderComponent>(connection.Provider)));
                }
                var callbacks = 0; hook.NodesRebuiltAction = _ => callbacks++;
                Assert.That(stages.Verify(stage), Is.True);
                var repeated = stages.TryInitialize(stage);
                Assert.That(repeated.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), repeated.Issue?.Code);
                Assert.That(stage.InitialElectrical, Is.SameAs(receipt)); Assert.That(callbacks, Is.Zero);
            }
            finally { hook.NodesRebuiltAction = null; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task CancellationDuringNodeRebuildRetiresTheWholeSceneWithoutPublishingEvidence()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var generated = Generate(true); var hook = Pair.Server.System<KsProcgenInsertionTestSystem>();
            using var cancellation = new CancellationTokenSource();
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code); var stage = generated.Stage!;
                hook.NodesRebuiltAction = _ => cancellation.Cancel();
                var result = stages.TryInitialize(stage, cancellationToken: cancellation.Token);
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Cancelled), result.Issue?.Code);
                Assert.That(stage.Active, Is.False); Assert.That(stage.InitialElectrical, Is.Null);
            }
            finally { hook.NodesRebuiltAction = null; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("cable-anchor")] [TestCase("receiver-range")] [TestCase("provider-range")]
    public async Task RepeatedVerificationRejectsNativeDriftWithoutRepairingOrReplacingHistoricalEvidence(string drift)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet(); var hook = Pair.Server.System<KsProcgenInsertionTestSystem>();
            try
            {
                var generated = Generate(true); Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!;
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), initialized.Issue?.Code);
                var receipt = stage.InitialElectrical!; var connection = receipt.Connections.Single(); var callbacks = 0;
                hook.NodesRebuiltAction = _ => callbacks++;
                if (drift == "cable-anchor") Pair.Server.System<SharedTransformSystem>().Unanchor(
                    stage.Entities.First(uid => SEntMan.HasComponent<CableComponent>(uid)));
                else if (drift == "receiver-range") Pair.Server.System<ExtensionCableSystem>()
                    .SetReceiverReceptionRange(connection.Consumer, connection.ReceiverReceptionRange + 1);
                else Pair.Server.System<ExtensionCableSystem>()
                    .SetProviderTransferRange(connection.Provider, connection.ProviderTransferRange + 1);
                Assert.That(stages.Verify(stage), Is.False);
                Assert.That(callbacks, Is.Zero);
                var repeated = stages.TryInitialize(stage);
                Assert.That(repeated.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected), repeated.Issue?.Code);
                Assert.That(stage.Active, Is.False); Assert.That(stage.InitialElectrical, Is.SameAs(receipt));
                Assert.That(callbacks, Is.Zero);
            }
            finally { hook.NodesRebuiltAction = null; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ElectricalAllowanceIsSharedAcrossTheWholeInitializationAndHasAnExactBoundary(bool shortByOne)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var probe = Generate(true); Assert.That(probe.Stage, Is.Not.Null, probe.Issue?.Code);
                var first = stages.TryInitialize(probe.Stage!);
                Assert.That(first.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), first.Issue?.Code);
                var allowance = probe.Stage!.InitialElectrical!.Work; stages.Discard(probe.Stage);
                var candidate = Generate(true, stageBudgets: new() { MaximumElectricalWork = allowance - (shortByOne ? 1 : 0) });
                Assert.That(candidate.Stage, Is.Not.Null, candidate.Issue?.Code); var stage = candidate.Stage!;
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(shortByOne ? KsProcgenOwnedStageStatus.BudgetExceeded :
                    KsProcgenOwnedStageStatus.InitializedPreview), initialized.Issue?.Code);
                if (shortByOne) { Assert.That(stage.Active, Is.False); Assert.That(stage.InitialElectrical, Is.Null); }
                else Assert.That(stage.InitialElectrical!.Work, Is.EqualTo(allowance));
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task NodeCallbacksCannotUnpauseOrMoveTheOwnedGridAndPublishAReceipt(bool move)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var hook = Pair.Server.System<KsProcgenInsertionTestSystem>(); var calls = 0;
            try
            {
                var generated = Generate(true); Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!;
                hook.NodesRebuiltAction = _ =>
                {
                    if (calls++ != 0) return;
                    if (move) Pair.Server.System<SharedTransformSystem>().SetLocalPosition(stage.GridUid, new(1f, 0f));
                    else Pair.Server.System<SharedMapSystem>().SetPaused(stage.MapId, false);
                };
                var initialized = stages.TryInitialize(stage);
                Assert.That(calls, Is.GreaterThan(0));
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected), initialized.Issue?.Code);
                Assert.That(stage.Active, Is.False); Assert.That(stage.InitialElectrical, Is.Null);
            }
            finally { hook.NodesRebuiltAction = null; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ConvertersRespectTheirNativeMountKindAndRejectLossOfWallSupport(bool wallSubstation)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var generated = Generate(true, wallSubstation: wallSubstation);
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code); var stage = generated.Stage!;
                var plan = stage.Electrical!;
                Assert.That(plan.Devices.Single(device => device.Substation).WallMounted, Is.EqualTo(wallSubstation));
                Assert.That(plan.Devices.Single(device => !device.Substation).WallMounted, Is.True);
                foreach (var device in plan.Devices.Where(device => device.WallMounted))
                {
                    Assert.That(stage.Scene.Entities.Any(intent => intent.Kind == KsProcgenOwnedSceneEntityKind.Wall &&
                        intent.Position == new System.Numerics.Vector2((float) device.Cell.X + 0.5f, (float) device.Cell.Y + 0.5f)), Is.True);
                    var region = generated.Proposal!.Furnishings.Single(item => item.RegionId == device.RegionId).Proposal.Layered!;
                    Assert.That(region.Access.Poses!.Members.Single(pose => device.Id == device.RegionId + "/" + pose.Support.MemberId)
                        .Support.Layer, Is.EqualTo(KsProcgenPlacementLayer.Wall));
                }
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), initialized.Issue?.Code);
                var receipt = stage.InitialElectrical;
                var apc = plan.Devices.Single(device => !device.Substation);
                var support = stage.Scene.Entities.Select((intent, index) => (intent, uid: stage.Entities[index]))
                    .Single(item => item.intent.Kind == KsProcgenOwnedSceneEntityKind.Wall && item.intent.Position ==
                        new System.Numerics.Vector2((float) apc.Cell.X + 0.5f, (float) apc.Cell.Y + 0.5f)).uid;
                Pair.Server.System<SharedTransformSystem>().Unanchor(support);
                Assert.That(stages.Verify(stage), Is.False);
                var repeated = stages.TryInitialize(stage);
                Assert.That(repeated.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected), repeated.Issue?.Code);
                Assert.That(repeated.Issue?.Code, Is.EqualTo("OwnedElectricalWallMountSupportDrift"));
                Assert.That(stage.InitialElectrical, Is.SameAs(receipt)); Assert.That(stage.Active, Is.False);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task MissingWallSupportCannotFallBackToAFloorMountedApc()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var generated = Generate(false, walls: false);
                Assert.That(generated.Stage, Is.Null);
                Assert.That(generated.Status, Is.Not.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview));
                Assert.That(generated.Issue, Is.Not.Null);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("none")] [TestCase("wire")] [TestCase("cancel")]
    public async Task GasAndWiringCommitEvidenceTogetherAfterAllNativeCallbacks(string fault)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var hook = Pair.Server.System<KsProcgenInsertionTestSystem>(); using var cancellation = new CancellationTokenSource();
            try
            {
                var generated = Generate(true, gas: true); Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var stage = generated.Stage!;
                var computer = stage.Assemblies.SelectMany(assembly => assembly.Values)
                    .Single(uid => SEntMan.HasComponent<ApcPowerReceiverComponent>(uid));
                var calls = 0;
                hook.GasTileChangedAction = uid =>
                {
                    if (uid != computer) return;
                    var position = SEntMan.GetComponent<TransformComponent>(uid).LocalPosition;
                    var cell = new Vector2i((int) Math.Floor((double) position.X), (int) Math.Floor((double) position.Y));
                    if (Pair.Server.System<AtmosphereSystem>().GetTileMixture(stage.GridUid, stage.MapUid, cell) is not { Pressure: > 1f }) return;
                    hook.GasTileChangedAction = null; calls++;
                    Assert.That(stage.InitialElectrical, Is.Null); Assert.That(stage.InitialAtmosphere, Is.Null);
                    if (fault == "wire") Pair.Server.System<ExtensionCableSystem>().SetReceiverReceptionRange(computer,
                        SEntMan.GetComponent<ExtensionCableReceiverComponent>(computer).ReceptionRange + 1);
                    if (fault == "cancel") cancellation.Cancel();
                };
                var initialized = stages.TryInitialize(stage, cancellationToken: cancellation.Token);
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(initialized.Status, Is.EqualTo(fault switch
                { "none" => KsProcgenOwnedStageStatus.InitializedPreview, "cancel" => KsProcgenOwnedStageStatus.Cancelled,
                    _ => KsProcgenOwnedStageStatus.Rejected }), initialized.Issue?.Code);
                if (fault == "none")
                {
                    var electrical = stage.InitialElectrical; var atmosphere = stage.InitialAtmosphere;
                    Assert.That(electrical, Is.Not.Null); Assert.That(atmosphere, Is.Not.Null);
                    Assert.That(stages.Verify(stage), Is.True);
                    Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                    Assert.That(stage.InitialElectrical, Is.SameAs(electrical)); Assert.That(stage.InitialAtmosphere, Is.SameAs(atmosphere));
                }
                else
                {
                    Assert.That(stage.Active, Is.False); Assert.That(stage.InitialElectrical, Is.Null); Assert.That(stage.InitialAtmosphere, Is.Null);
                    if (fault == "wire") Assert.That(initialized.Issue?.Code, Is.EqualTo("OwnedElectricalReceiptDrift"));
                }
            }
            finally { hook.GasTileChangedAction = null; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task WholeStageInitializationLeavesForeignPendingNativeGroupsUntouched()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var maps = Pair.Server.System<SharedMapSystem>();
            var groups = Pair.Server.System<NodeGroupSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var map = maps.CreateMap(out var mapId, runMapInit: false); maps.SetPaused(mapId, true);
            var grid = maps.CreateGridEntity(mapId); grid.Comp.CanSplit = false;
            maps.SetTile(grid, Vector2i.Zero, new Tile(Pair.Server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId));
            var foreign = SEntMan.SpawnEntity("CableHV", new EntityCoordinates(grid.Owner, new(0.5f, 0.5f)));
            maps.InitializeMap(mapId, unpause: false); groups.ForceUpdate();
            var node = SEntMan.GetComponent<NodeContainerComponent>(foreign).Nodes.Values.Single(); var original = node.NodeGroup;
            Assert.That(original, Is.Not.Null); groups.QueueReflood(node); Assert.That(node.FlaggedForFlood, Is.True);
            try
            {
                var generated = Generate(true); Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code);
                var initialized = stages.TryInitialize(generated.Stage!);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), initialized.Issue?.Code);
                Assert.That(stages.Verify(generated.Stage!), Is.True);
                Assert.That(node.NodeGroup, Is.SameAs(original)); Assert.That(node.FlaggedForFlood, Is.True);
            }
            finally { stages.DiscardAll(); maps.DeleteMap(mapId); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task CompactInteriorCanSelectAWallSubstationFromTheWholeVariantPool()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var generated = Generate(true, compact: true);
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.Code); var stage = generated.Stage!;
                Assert.That(generated.Proposal!.Partition!.FloorCells.Count, Is.EqualTo(2));
                Assert.That(stage.Electrical!.Devices.Single(device => device.Substation).PrototypeId, Is.EqualTo("SubstationWallBasic"));
                Assert.That(stage.Electrical.Devices.All(device => device.WallMounted), Is.True);
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), initialized.Issue?.Code);
                Assert.That(stage.InitialElectrical!.Connections.Count, Is.EqualTo(1));
                Assert.That(stages.Verify(stage), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenOwnedStageResult Generate(bool computer, KsProcgenOwnedStageBudgets? stageBudgets = null,
        bool wallSubstation = false, bool walls = true, bool gas = false, bool compact = false)
    {
        var request = new KsProcgenRequest
        {
            SchemaVersion = 2, RequestId = "ElectricalPlanning", Seed = 17,
            ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland, ElectricalProfile = "KsProcgenWiredElectrical", EntranceDomain = new(),
            Shape = new() { AddRectangles = new() { new() { Min = new(0, 0), Max = new(6, 6) } },
                EnvelopeCells = Enumerable.Range(-1, 8).SelectMany(x => Enumerable.Range(-1, 8)
                    .Select(y => new Robust.Shared.Maths.Vector2i(x, y))).Where(cell => cell.X is -1 or 6 || cell.Y is -1 or 6).ToList() },
        };
        if (!walls) request.Shape.EnvelopeCells.Clear();
        if (gas) request.AtmosphereProfile = "KsProcgenStandardAir";
        if (wallSubstation)
        {
            request.ElectricalProfile = null;
            request.Electrical = new() { Mode = KsProcgenElectricalMode.AutoDistribute,
                SupplyRequirement = KsProcgenElectricalSupply.WiredOnly,
                Substations = new() { new() { Prototype = "SubstationWallBasic" } } };
        }
        if (compact)
        {
            request.Shape.AddRectangles.Single().Max = new(1, 2);
            request.Shape.EnvelopeCells = Enumerable.Range(-1, 3).SelectMany(x => Enumerable.Range(-1, 4)
                .Select(y => new Vector2i(x, y))).Where(cell => cell.X is -1 or 1 || cell.Y is -1 or 2).ToList();
            request.WindowGoal.ExteriorWindowFraction = 0f;
            request.ElectricalProfile = null;
            request.Electrical = new() { Mode = KsProcgenElectricalMode.AutoDistribute, SupplyRequirement = KsProcgenElectricalSupply.WiredOnly,
                Substations = new() { new() { Prototype = "SubstationBasic", Weight = 1000f }, new() { Prototype = "SubstationWallBasic" } } };
        }
        return Pair.Server.System<KsProcgenOwnedStageSystem>().TryGenerate(request,
            computer ? "ElectricalRoom" : "KsProcgenEmptyOfficeThemeFixture",
            planningBudgets: new() { MinimumRoomCells = compact ? 1 : 4, PreferredMaximumRoomCells = 64,
                MaximumFurnishingProbes = 16384 }, stageBudgets: stageBudgets,
            content: computer ? KsProcgenElectricalPlanningTests.Catalog(Pair.Server.ResolveDependency<IPrototypeManager>()) : null);
    }
}
