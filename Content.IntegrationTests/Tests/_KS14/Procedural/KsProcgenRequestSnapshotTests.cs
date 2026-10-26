using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenRequestSnapshot))]
public sealed class KsProcgenRequestSnapshotTests : KsProcgenGameTest
{
    [Test]
    public async Task OwnedPlannerUsesRetainedInputAfterTheOriginalIsEdited()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var request = new KsProcgenRequest
            {
                RequestId = "OwnedSnapshot", Seed = 23, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
                Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } },
                EntranceDomain = new()
                {
                    Entrances = new() { new() { PortId = "West", ThresholdCells = new() { new(-1, 0) }, InwardNormal = new(1, 0) } },
                },
            };
            var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(),
                new HashSet<Vector2i> { new(-1, 0), new(-2, 0) }, new HashSet<Vector2i>(), true, true);
            var themeId = "KsProcgenSimpleOffice";
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var snapshot, out var issue), Is.True, issue?.Code);
            var budgets = new KsProcgenOwnedPlanBudgets { PreferredMaximumRoomCells = 64 };
            var before = SEntMan.GetEntities().ToHashSet();
            var direct = KsProcgenOwnedInteriorPlanner.Plan(manager, snapshot!.CopyRequest(), themeId, context, [], budgets: budgets);
            Assert.That(direct.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), direct.Issue?.Code);
            request.Shape.Cells.Clear();
            request.EntranceDomain.Entrances.Clear();
            request.FallbackPolicy.AllowSparseLighting = false;
            var retained = KsProcgenOwnedInteriorPlanner.PlanSnapshot(manager, snapshot, themeId, context, [], budgets: budgets);
            Assert.That(retained.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), retained.Issue?.Code);
            Assert.That(retained.SemanticHash, Is.EqualTo(direct.SemanticHash));
            Assert.That(retained.ExpandedCells, Is.EqualTo(direct.ExpandedCells));
            Assert.That(retained.Published || retained.EngineAccessVerified || retained.GasClosureVerified || retained.WorkingLightingVerified, Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task LoadedUtilityDeclarationsArePinnedAndRoundtripWithoutPrototypeAliases()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            var gasId = "KsProcgenStandardAir";
            var powerId = "KsProcgenAutomaticElectrical";
            var gas = manager.Index<KsProcgenAtmosphereProfilePrototype>(gasId);
            var power = manager.Index<KsProcgenElectricalProfilePrototype>(powerId);
            var pressure = gas.PressureKPa;
            var margin = power.DemandMargin;
            var request = new KsProcgenRequest
            {
                RequestId = "PinnedInput", Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } },
                AtmosphereProfile = gasId, ElectricalProfile = powerId,
            };
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var snapshot, out var issue), Is.True, issue?.Code);
                gas.PressureKPa = 200f;
                power.DemandMargin = 0.8f;
                Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var changed, out issue), Is.True, issue?.Code);
                Assert.That(changed!.DeclarationHash, Is.Not.EqualTo(snapshot!.DeclarationHash));
                Assert.That(snapshot.Atmosphere!.PressureKPa, Is.EqualTo(pressure));
                Assert.That(snapshot.Electrical!.DemandMargin, Is.EqualTo(margin));
                var portable = snapshot.CopyRequest();
                var node = serialization.WriteValue(portable, alwaysWrite: true);
                var restored = serialization.Read<KsProcgenRequest>(node, notNullableOverride: true);
                Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, restored, out var replay, out issue), Is.True, issue?.Code);
                Assert.That(replay!.DeclarationHash, Is.EqualTo(snapshot.DeclarationHash));
                Assert.That(KsProcgenRequestSnapshot.TryCapture(restored, out var headless, out issue), Is.True, issue?.Code);
                Assert.That(headless!.DeclarationHash, Is.EqualTo(snapshot.DeclarationHash));
                var themeId = "KsProcgenSimpleOffice";
                var geometry = KsProcgenGeometryPipeline.Plan(manager, portable, [], themeId);
                Assert.That(geometry.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.InvalidInput));
                Assert.That(geometry.Issue?.Code, Is.EqualTo("AtmospherePlanningUnsupported"));
                Assert.That(geometry.Packing, Is.Null);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally { gas.PressureKPa = pressure; power.DemandMargin = margin; }
        });
    }

    [Test]
    public async Task DraftReferencesAndExplicitOverridesUseTheSameSnapshotContract()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var spec = new KsProcgenUtilityDraftSpec
            {
                DocumentId = "RequestEditor",
                Atmosphere = new() { ["Local"] = new() },
                Electrical = new() { ["Local"] = new() { DemandMargin = 0.6f } },
            };
            Assert.That(KsProcgenUtilityDraft.TrySnapshot(spec, id => manager.HasIndex<GasPrototype>(id),
                id => manager.HasIndex<EntityPrototype>(id), out var draft, out var issue), Is.True, issue?.Code);
            var request = new KsProcgenRequest
            {
                RequestId = "DraftRequest", Shape = new() { Cells = new() { new(0, 0) } },
                AtmosphereProfile = "Local", ElectricalProfile = "Local",
            };
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var snapshot, out issue, draft: draft), Is.True, issue?.Code);
            Assert.That(snapshot!.Atmosphere!.Mode, Is.EqualTo(KsProcgenAtmosphereMode.Preserve));
            Assert.That(snapshot.Electrical!.DemandMargin, Is.EqualTo(0.6f));
            Assert.That(KsProcgenRequestSnapshot.TryCapture(snapshot.CopyRequest(), out var headless, out issue), Is.True, issue?.Code);
            Assert.That(headless!.DeclarationHash, Is.EqualTo(snapshot.DeclarationHash));
            request.Electrical = new() { DemandMargin = 0.1f };
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var overridden, out issue, draft: draft), Is.True, issue?.Code);
            Assert.That(overridden!.Electrical!.DemandMargin, Is.EqualTo(0.1f));
            request.AtmosphereProfile = "Missing";
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var rejected, out issue, draft: draft), Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(issue?.Code, Is.EqualTo("UnknownAtmosphereProfile"));
        });
    }

    [Test]
    public async Task LegacySerializationDefaultsToVersionOneAndFutureSchemaIsRejected()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            var request = new KsProcgenRequest { RequestId = "LegacyInput", Shape = new() { Cells = new() { new(0, 0) } } };
            var node = (MappingDataNode) serialization.WriteValue(request, alwaysWrite: true);
            node.Remove("schemaVersion");
            var legacy = serialization.Read<KsProcgenRequest>(node, notNullableOverride: true);
            Assert.That(legacy.SchemaVersion, Is.EqualTo(1));
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, legacy, out var snapshot, out var issue), Is.True, issue?.Code);
            Assert.That(snapshot!.Atmosphere, Is.Null);
            Assert.That(snapshot.Electrical, Is.Null);
            node.Add("schemaVersion", new ValueDataNode("3"));
            var future = serialization.Read<KsProcgenRequest>(node, notNullableOverride: true);
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, future, out var rejected, out issue), Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(issue?.Code, Is.EqualTo("UnsupportedRequestSchema"));
            Assert.That(KsProcgenPackingPlanner.Plan(future, []).Issue?.Code, Is.EqualTo("UnsupportedRequestSchema"));
            future.AtmosphereProfile = "Missing";
            var themeId = "KsProcgenSimpleOffice";
            var result = KsProcgenGeometryPipeline.Plan(manager, future, [], themeId);
            Assert.That(result.Issue?.Code, Is.EqualTo("UnsupportedRequestSchema"));
            Assert.That(result.Packing, Is.Null);
        });
    }
}
