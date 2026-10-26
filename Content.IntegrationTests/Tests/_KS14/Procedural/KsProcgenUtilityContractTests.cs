using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenUtilityContract))]
public sealed class KsProcgenUtilityContractTests : KsProcgenGameTest
{
    [Test]
    public async Task NamedScopedProfilesArePinnedAcrossSerializationAndPrototypeEdits()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var serializer = Pair.Server.ResolveDependency<ISerializationManager>();
            var profileId = "KsProcgenStandardAir";
            var source = manager.Index<KsProcgenAtmosphereProfilePrototype>(profileId);
            var pressure = source.PressureKPa;
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var request = Spec();
                request.Utilities!.GasAssignments.Add(new() { Id = "Air", Scope = KsProcgenUtilityScope.Area, ProfileId = source.ID });
                Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var snapshot, out var issue), Is.True, issue?.Code);
                source.PressureKPa = 200f;
                var portable = snapshot!.CopyRequest();
                Assert.That(portable.Utilities!.GasAssignments[0].ScopeId, Is.EqualTo(request.RequestId));
                var restored = serializer.Read<KsProcgenRequest>(serializer.WriteValue(portable, alwaysWrite: true), notNullableOverride: true);
                Assert.That(KsProcgenRequestSnapshot.TryCapture(restored, out var replay, out issue), Is.True, issue?.Code);
                Assert.That(replay!.DeclarationHash, Is.EqualTo(snapshot.DeclarationHash));
                Assert.That(replay.Utilities!.GasAssignments[0].Profile.PressureKPa, Is.EqualTo(pressure));
                Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, restored, out var loaded, out issue), Is.True, issue?.Code);
                Assert.That(loaded!.DeclarationHash, Is.EqualTo(snapshot.DeclarationHash));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally { source.PressureKPa = pressure; }
        });
    }

    [Test]
    public async Task DraftGasAssignmentsResolveWithoutGlobalPrototypeRegistration()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(KsProcgenAtmosphereProfiles.TryResolve(manager, "KsProcgenStandardAir", out var gas, out var issue), Is.True, issue?.Code);
            var spec = new KsProcgenUtilityDraftSpec { DocumentId = "ScopedDraft", Atmosphere = new() { ["LocalAir"] = gas!.CopySpec() } };
            Assert.That(KsProcgenUtilityDraft.TrySnapshot(spec, id => manager.HasIndex<GasPrototype>(id),
                id => manager.HasIndex<EntityPrototype>(id), out var draft, out issue), Is.True, issue?.Code);
            var request = Spec();
            request.Utilities!.GasAssignments.Add(new() { Id = "Air", Scope = KsProcgenUtilityScope.Room, ScopeId = "Room/0", ProfileId = "LocalAir" });
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var snapshot, out issue, draft: draft), Is.True, issue?.Code);
            spec.Atmosphere["LocalAir"].PressureKPa = 200f;
            Assert.That(snapshot!.Utilities!.GasAssignments[0].Profile.PressureKPa, Is.EqualTo(gas.PressureKPa));
            Assert.That(snapshot.Utilities.RequiresTargetBinding, Is.True);
            var localProfileId = "LocalAir";
            Assert.That(manager.HasIndex<KsProcgenAtmosphereProfilePrototype>(localProfileId), Is.False);
        });
    }

    [Test]
    public async Task UnknownPrototypeConsumerFailsReceivingCapture()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var request = Spec();
            request.Utilities!.Consumers.Add(new() { Id = "Missing", Selector = KsProcgenConsumerSelector.Prototype, SelectorId = "KsProcgenDefinitelyMissingConsumer" });
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out var snapshot, out var issue), Is.False);
            Assert.That(snapshot, Is.Null);
            Assert.That(issue!.Code, Is.EqualTo("UnknownConsumerPrototype"));
            request.Utilities.Consumers[0].SelectorId = "ComputerAlert";
            Assert.That(KsProcgenRequestSnapshot.TryCapture(manager, request, out snapshot, out issue), Is.True, issue?.Code);
            Assert.That(snapshot!.Utilities!.RequiresTargetBinding, Is.True);
        });
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    public async Task PlannersRejectUnrealizedScopedRequirementsBeforeAnyWork(int declaration)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var request = Spec();
            switch (declaration)
            {
                case 0: request.Utilities!.Consumers.Add(new() { Id = "All" }); break;
                case 1: request.Utilities!.GasWriteCells = new(); break;
                case 2: request.Utilities!.GasRestrictions.MaximumPressureKPa = 120f; break;
                case 3: request.Utilities!.ElectricalRestrictions.ForbidBatteryOnly = true; break;
            }
            var before = SEntMan.GetEntities().ToHashSet();
            var geometry = KsProcgenGeometryPipeline.Plan(manager, request, [], "KsProcgenSimpleOffice");
            Assert.That(geometry.Status, Is.EqualTo(KsProcgenGeometryPipelineStatus.InvalidInput));
            Assert.That(geometry.Issue?.Code, Is.EqualTo("UtilityPlanningUnsupported"));
            Assert.That(geometry.Packing, Is.Null);
            var context = new KsProcgenGroupRouteContext(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true);
            var owned = KsProcgenOwnedInteriorPlanner.Plan(manager, request, "KsProcgenSimpleOffice", context, []);
            Assert.That(owned.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.InvalidInput));
            Assert.That(owned.Issue?.Code, Is.EqualTo("UtilityPlanningUnsupported"));
            Assert.That(owned.Stages, Is.Empty);
            Assert.That(owned.LayoutProbes, Is.Zero);
            Assert.That(owned.ExpandedCells, Is.Zero);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private static KsProcgenRequest Spec() => new()
    {
        SchemaVersion = 2, RequestId = "ScopedUtilities", Utilities = new(),
        Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } },
    };
}
