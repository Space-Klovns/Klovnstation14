#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenAssemblyPlacementSystem))]
public sealed class KsProcgenRotationTests : KsProcgenGameTest
{
    [TestCase(0, 0.005f, 0.10f)] [TestCase(1, 0.10f, -0.005f)]
    [TestCase(2, -0.005f, -0.10f)] [TestCase(3, -0.10f, 0.005f)]
    public async Task AsymmetricNativeFixtureRealizesClockwiseAuthoringTurns(int turn, float centerX, float centerY)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var result = Stage("KsProcgenHardContactHotplateFixture", turn);
            try
            {
                Assert.That(result.Stage, Is.Not.Null, message: result.Issue?.Code);
                var stage = result.Stage!; var uid = stage.Members["Machine"];
                var (position, rotation) = Pair.Server.System<SharedTransformSystem>().GetWorldPositionRotation(uid);
                var fixture = SEntMan.GetComponent<FixturesComponent>(uid).Fixtures["itemcollide"];
                // Native collision geometry, with independent expected centers from the asymmetric
                // bounds (-.14,-.05)-(.15,.25). A symmetric square would conceal the wrong sign.
                var bounds = fixture.Shape.ComputeAABB(new Transform(position, rotation), 0);
                var center = bounds.Center - position;
                Assert.That(center.X, Is.EqualTo(centerX).Within(0.00001f));
                Assert.That(center.Y, Is.EqualTo(centerY).Within(0.00001f));
                Assert.That(stages.Verify(stage), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ComputerAtRightWallFacesItsDeclaredLeftApproachInTheEngine()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var floor = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3).Select(y => new Vector2i(x, y))).ToHashSet();
            var facing = KsProcgenInteractionFacingPlanner.ChooseMachineFacing(new(2, 2), floor,
                new HashSet<Vector2i> { new(3, 2) }, new HashSet<Vector2i>(), new HashSet<Vector2i>(),
                new HashSet<Vector2i> { new(0, 0) }, new[] { 1 }, 17, "RightWallComputer");
            Assert.That(facing.Facing!.Facing, Is.EqualTo(new Vector2i(-1, 0)));
            var result = Stage("KsProcgenFilteredComputerFixture", facing.Facing.QuarterTurns);
            try
            {
                Assert.That(result.Stage, Is.Not.Null, message: result.Issue?.Code);
                var (_, rotation) = Pair.Server.System<SharedTransformSystem>().GetWorldPositionRotation(result.Stage!.Members["Machine"]);
                var actualFront = rotation.RotateVec(new Vector2(0f, -1f));
                Assert.That(actualFront.X, Is.EqualTo(-1f).Within(0.00001f));
                Assert.That(actualFront.Y, Is.EqualTo(0f).Within(0.00001f));
                Assert.That(SpriteComponent.Layer.GetDirection(RsiDirectionType.Dir4, rotation.Reduced().FlipPositive()),
                    Is.EqualTo(RsiDirection.West));
                Assert.That(facing.Facing.Approach, Is.EqualTo(new Vector2i(1, 2)));
                Assert.That(facing.Facing.CleanPath, Is.Not.Empty);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(1, 0.10f, -0.005f)] [TestCase(3, -0.10f, 0.005f)]
    public async Task SurfaceAdapterAppliesTheSameClockwiseRotationToNativeGeometry(int turn, float x, float y)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenSurfaceStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var result = stages.TryStage("Table", "KsProcgenHardContactHotplateFixture", subjectQuarterTurns: turn);
            try
            {
                Assert.That(result.Stage, Is.Not.Null, message: result.Issue?.Code); var stage = result.Stage!;
                void Check()
                {
                    var (position, rotation) = Pair.Server.System<SharedTransformSystem>().GetWorldPositionRotation(stage.SubjectUid);
                    var geometry = SEntMan.GetComponent<FixturesComponent>(stage.SubjectUid).Fixtures["itemcollide"].Shape;
                    var center = geometry.ComputeAABB(new Transform(position, rotation), 0).Center - position;
                    Assert.That(center.X, Is.EqualTo(x).Within(0.00001f)); Assert.That(center.Y, Is.EqualTo(y).Within(0.00001f));
                    Assert.That(stages.Verify(stage), Is.True);
                }
                Check(); Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenSurfaceStageStatus.InitializedPreview)); Check();
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(1, -1f)] [TestCase(3, 1f)]
    public async Task ContainedMemberInheritsTheClockwiseParentTurnExactlyOnce(int turn, float x)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenAssemblyStageSystem>(); var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("ContainerRotation", new()
            {
                Id = "Base", AnchorMember = "Machine",
                Members = [new() { Id = "Machine", Entity = "KsProcgenRotatableMicrowaveFixture", RotationMode = KsProcgenMemberRotation.Independent },
                    new() { Id = "Stored", Entity = "Paper", RotationMode = KsProcgenMemberRotation.Independent }],
                Relations = [new() { Id = "Inside", Subject = "Stored", Target = "Machine", Kind = KsProcgenRelationKind.InContainer,
                    ContainerId = "microwave_entity_container" }],
            }, new Dictionary<string, string>(), id => manager.TryIndex<EntityPrototype>(id, out _),
                out var assembly, out var issue), Is.True, message: issue?.Code);
            var staged = stages.TryStage(assembly!, new[] { "Machine", "Stored" },
                new[] { new KsProcgenFloorRootPose("Machine", new Vector2(2.5f, 2.5f)) },
                new[] { new KsProcgenMemberOrientation("Machine", turn), new KsProcgenMemberOrientation("Stored", turn) }, 0);
            try
            {
                Assert.That(staged.Stage, Is.Not.Null, message: staged.Issue?.Code); var stage = staged.Stage!;
                void Check()
                {
                    var stored = SEntMan.GetComponent<TransformComponent>(stage.Members["Stored"]);
                    Assert.That(stored.LocalRotation, Is.EqualTo(Angle.Zero));
                    Assert.That(stored.ParentUid, Is.EqualTo(stage.Members["Machine"]));
                    var (_, rotation) = Pair.Server.System<SharedTransformSystem>().GetWorldPositionRotation(stage.Members["Stored"]);
                    var front = rotation.RotateVec(new Vector2(0f, -1f));
                    Assert.That(front.X, Is.EqualTo(x).Within(0.00001f)); Assert.That(front.Y, Is.EqualTo(0f).Within(0.00001f));
                    Assert.That(stages.Verify(stage), Is.True);
                }
                Check(); Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview)); Check();
            }
            finally { stages.DiscardAll(); }
        });
    }

    [Test]
    public async Task CompleteOwnedMaterializationRealizesTheAsymmetricClockwisePose()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var spec = new KsProcgenContentDraftSpec
            {
                DocumentId = "OwnedRotation",
                Themes = new() { ["Rotation"] = new() { Parent = "KsProcgenSimpleOffice", LightingPacks = new(),
                    Goals = new() { FurnishingDensity = 0f },
                    EntityPacks = new() { new() { Pack = "Machines" } } } },
                Entities = new() { ["Machines"] = new() { Assemblies = new() { new() { Id = "Machine", Assembly = "Machine", MinimumCount = 1 } } } },
                Assemblies = new() { ["Machine"] = new() { AnchorMember = "Machine",
                    Members = new() { new() { Id = "Machine", Entity = "KsProcgenHardContactHotplateFixture",
                        AllowedQuarterTurns = new() { 1 }, LocalQuarterTurns = 1, RotationMode = KsProcgenMemberRotation.Independent } } } },
            };
            Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, message: issue?.Code);
            Assert.That(KsProcgenThemeCatalog.TryCapture(manager, "Rotation", out var content, out issue, assets: draft), Is.True, message: issue?.Code);
            var request = new KsProcgenRequest { SchemaVersion = 2, RequestId = "OwnedRotation", Seed = 17,
                ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
                Shape = new() { Cells = Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Vector2i(x, y))).ToList() } };
            var generated = stages.TryGenerate(request, "Rotation", content: content,
                planningBudgets: new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 });
            try
            {
                Assert.That(generated.Stage, Is.Not.Null, message: generated.Issue?.Code); var stage = generated.Stage!;
                var machines = stage.Entities.Concat(stage.Assemblies.SelectMany(assembly => assembly.Values))
                    .Where(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "KsProcgenHardContactHotplateFixture").ToArray();
                Assert.That(machines, Is.Not.Empty);
                void Check()
                {
                    foreach (var uid in machines)
                    {
                        var (position, rotation) = Pair.Server.System<SharedTransformSystem>().GetWorldPositionRotation(uid);
                        var geometry = SEntMan.GetComponent<FixturesComponent>(uid).Fixtures["itemcollide"].Shape;
                        var center = geometry.ComputeAABB(new Transform(position, rotation), 0).Center - position;
                        Assert.That(center.X, Is.EqualTo(0.10f).Within(0.00001f)); Assert.That(center.Y, Is.EqualTo(-0.005f).Within(0.00001f));
                    }
                    Assert.That(stages.Verify(stage), Is.True);
                }
                Check(); Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview)); Check();
            }
            finally { stages.DiscardAll(); }
        });
    }

    private KsProcgenAssemblyStageResult Stage(string prototype, int turn)
    {
        var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("RotationRegression", new()
        {
            Id = "Base", AnchorMember = "Machine",
            Members = [new() { Id = "Machine", Entity = prototype, RotationMode = KsProcgenMemberRotation.Independent }],
        }, new Dictionary<string, string>(), id => manager.TryIndex<EntityPrototype>(id, out _),
            out var assembly, out var issue), Is.True, message: issue?.Code);
        return Pair.Server.System<KsProcgenAssemblyStageSystem>().TryStage(assembly!, new[] { "Machine" },
            new[] { new KsProcgenFloorRootPose("Machine", new Vector2(2.5f, 2.5f)) },
            new[] { new KsProcgenMemberOrientation("Machine", turn) }, 0);
    }
}
