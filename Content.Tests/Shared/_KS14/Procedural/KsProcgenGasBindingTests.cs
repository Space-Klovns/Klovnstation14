#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenGasBindingTests
{
    private static readonly Vector2i[] Cells = [new(0, 0), new(1, 0), new(2, 0)];
    private static readonly KsProcgenGasRoomTarget[] Rooms = [new("A", [Cells[0]], null), new("B", [Cells[2]], null)];
    private static readonly KsProcgenGasDomainTarget[] SharedDomain = [new("D", Cells)];

    [Test]
    public void ScopedOverridesBindWithoutMergingMixturesAndRejectSharedConflictingStates()
    {
        var request = Request();
        Add(request, "RequestAir", KsProcgenUtilityScope.Request, null, Fill());
        Add(request, "AreaNitrogen", KsProcgenUtilityScope.Area, null, Fill(nitrogen: true));
        Add(request, "RoomAir", KsProcgenUtilityScope.Room, "A", Fill());
        var snapshot = Capture(request);
        var result = KsProcgenGasBindings.Bind(snapshot, Rooms, [new("Left", [Cells[0], Cells[1]]), new("Right", [Cells[2]])]);
        Assert.That(result.Status, Is.EqualTo(KsProcgenGasBindingStatus.Bound), message: result.Issue?.Code);
        Assert.That(result.Domains[0].Demands.Single().AssignmentId, Is.EqualTo("RoomAir"));
        Assert.That(result.Domains[1].NominalProfile!.MoleFractions, Is.EqualTo(new[] { new KsProcgenGasFraction("Nitrogen", 1f) }));
        var conflict = KsProcgenGasBindings.Bind(snapshot, Rooms, SharedDomain);
        AssertFailure(conflict, "ConflictingGasDomainStates");
        Assert.That(conflict.Issue!.Message, Does.Contain("RoomAir").And.Contain("AreaNitrogen"));
    }

    [Test]
    public void RoomAndDomainHaveEqualPriorityAndDiagnosticNamesBothAssignments()
    {
        var request = Request();
        Add(request, "RoomAir", KsProcgenUtilityScope.Room, "A", Fill());
        Add(request, "DomainNitrogen", KsProcgenUtilityScope.GasDomain, "D", Fill(nitrogen: true));
        var result = KsProcgenGasBindings.Bind(Capture(request), Rooms, SharedDomain);
        AssertFailure(result, "ConflictingBoundGasAssignments");
        Assert.That(result.Issue!.Message, Does.Contain("RoomAir").And.Contain("DomainNitrogen"));
    }

    [Test]
    public void PreferredConflictIsReportedWhileCompatibleVerificationObligationsStaySeparate()
    {
        var request = Request();
        Add(request, "Hard", KsProcgenUtilityScope.Room, "A", Fill());
        var preferred = Fill(nitrogen: true); preferred.Requirement = KsProcgenAtmosphereRequirement.Preferred;
        Add(request, "Soft", KsProcgenUtilityScope.Room, "B", preferred);
        var result = KsProcgenGasBindings.Bind(Capture(request), Rooms, SharedDomain);
        Assert.That(result.Status, Is.EqualTo(KsProcgenGasBindingStatus.Bound));
        Assert.That(result.Domains.Single().Demands.Single().AssignmentId, Is.EqualTo("Hard"));
        Assert.That(result.Omissions.Single(), Is.EqualTo(new KsProcgenGasOmission("Soft", "D", "PreferredGasDomainConflict") { RoomId = "B" }));
        preferred.MoleFractions = Fill().MoleFractions;
        preferred.StateRequirement = KsProcgenAtmosphereState.StableAtRest; preferred.ObservationTicks = 10;
        preferred.PressureToleranceKPa = 0.01f;
        request.Utilities!.GasAssignments[1].Profile = preferred;
        result = KsProcgenGasBindings.Bind(Capture(request), Rooms, SharedDomain);
        Assert.That(result.Domains.Single().Demands.Count, Is.EqualTo(2));
        Assert.That(result.Domains.Single().NominalProfile!.StateRequirement, Is.EqualTo(KsProcgenAtmosphereState.InitialOnly));
        Assert.That(result.Domains.Single().Demands.Single(demand => demand.AssignmentId == "Soft").Profile.ObservationTicks, Is.EqualTo(10));
        Assert.That(result.GasClosureVerified || result.InitialGasVerified, Is.False);
    }

    [TestCase(KsProcgenUtilityScope.Room)] [TestCase(KsProcgenUtilityScope.GasDomain)]
    public void UnknownTargetsRejectTheWholeCandidate(KsProcgenUtilityScope scope)
    {
        var request = Request(); Add(request, "Missing", scope, "Unknown", Fill());
        AssertFailure(KsProcgenGasBindings.Bind(Capture(request), Rooms, SharedDomain), "UnknownGasAssignmentTarget");
    }

    [TestCase(false)] [TestCase(true)]
    public void GasWritesCannotCrossPreservedOrNarrowedOwnership(bool preserved)
    {
        var request = Request(); request.Atmosphere = Fill();
        if (preserved) request.Shape.PreservedCells = new() { Cells[1] };
        else request.Utilities!.GasWriteCells = new();
        AssertFailure(KsProcgenGasBindings.Bind(Capture(request), Rooms, SharedDomain), "GasDomainWritePermissionRequired");
    }

    [TestCase(false)] [TestCase(true)]
    public void RoomWithoutGasVolumeFailsRequiredFillAndReportsPreferredOmission(bool preferred)
    {
        var request = Request(); var fill = Fill();
        if (preferred) fill.Requirement = KsProcgenAtmosphereRequirement.Preferred;
        Add(request, "NoVolume", KsProcgenUtilityScope.Room, "A", fill);
        var result = KsProcgenGasBindings.Bind(Capture(request), Rooms, [new("Right", [Cells[2]])]);
        if (!preferred) { AssertFailure(result, "GasRoomHasNoVolume"); return; }
        Assert.That(result.Status, Is.EqualTo(KsProcgenGasBindingStatus.Bound));
        Assert.That(result.Omissions.Single(), Is.EqualTo(new KsProcgenGasOmission("NoVolume", null, "PreferredGasRoomHasNoVolume") { RoomId = "A" }));
    }

    [Test]
    public void UnassignedRoomCannotBeMutatedByItsNeighborsFill()
    {
        var request = Request(); Add(request, "RoomAir", KsProcgenUtilityScope.Room, "A", Fill());
        AssertFailure(KsProcgenGasBindings.Bind(Capture(request), Rooms, SharedDomain), "ConflictingGasDomainStates");
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)]
    public void HardRestrictionsCannotBeBypassedByRoomOverridesOrUnspecifiedState(int variant)
    {
        var request = Request();
        if (variant == 0) { request.Atmosphere = Fill(); request.Utilities!.GasRestrictions.MaximumPressureKPa = 80f; }
        if (variant == 1) { request.Atmosphere = new(); request.Utilities!.GasRestrictions.AllowedGases = new() { "Nitrogen" }; }
        if (variant == 2) request.Utilities!.GasRestrictions.AllowedModes = new() { KsProcgenAtmosphereMode.Fill };
        AssertFailure(KsProcgenGasBindings.Bind(Capture(request), Rooms, SharedDomain),
            variant == 2 ? "UnspecifiedGasRestrictionUnverified" : "GasAssignmentViolatesRestriction");
    }

    [Test]
    public void ExactBudgetReplaysAndOneLessStopsWithoutLeakingBindings()
    {
        var request = Request(); request.Atmosphere = Fill(); var snapshot = Capture(request);
        var result = KsProcgenGasBindings.Bind(snapshot, Rooms, SharedDomain);
        Assert.That(result.Status, Is.EqualTo(KsProcgenGasBindingStatus.Bound));
        Assert.That(KsProcgenGasBindings.Bind(snapshot, Rooms, SharedDomain, maximumWork: result.Work).BindingHash, Is.EqualTo(result.BindingHash));
        foreach (var allowance in new[] { 0, result.Work - 1 })
        {
            var exhausted = KsProcgenGasBindings.Bind(snapshot, Rooms, SharedDomain, maximumWork: allowance);
            Assert.That(exhausted.Status, Is.EqualTo(KsProcgenGasBindingStatus.BudgetExceeded));
            Assert.That(exhausted.Work, Is.LessThanOrEqualTo(allowance)); Assert.That(exhausted.Domains, Is.Empty);
        }
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    public void AmbiguousOrInvalidTargetMasksFailBeforeBinding(int variant)
    {
        IReadOnlyList<KsProcgenGasRoomTarget> rooms = Rooms; IReadOnlyList<KsProcgenGasDomainTarget> domains = SharedDomain;
        if (variant == 0) rooms = [Rooms[0], Rooms[0]];
        if (variant == 1) rooms = [Rooms[0], new("B", [Cells[0]], null)];
        if (variant == 2) domains = [SharedDomain[0], new("Other", [Cells[0]])];
        if (variant == 3) domains = [new("Outside", [new(9, 9)])];
        if (variant == 4) domains = [new("bad id", Cells)];
        var result = KsProcgenGasBindings.Bind(Capture(Request()), rooms, domains);
        Assert.That(result.Status, Is.EqualTo(KsProcgenGasBindingStatus.InvalidInput));
        Assert.That(result.Domains, Is.Empty); Assert.That(result.BindingHash, Is.Zero);
    }

    [Test]
    public void IdentityIsCanonicalAndOutputMasksAreDetached()
    {
        var request = Request(); request.Atmosphere = Fill(); var snapshot = Capture(request);
        var mutable = Cells.ToArray();
        var first = KsProcgenGasBindings.Bind(snapshot, Rooms, [new("D", mutable)]);
        mutable[0] = new(900, 900);
        var replay = KsProcgenGasBindings.Bind(snapshot, Rooms.Reverse().ToArray(), [new("D", Cells.Reverse().ToArray())]);
        Assert.That(first.BindingHash, Is.EqualTo(replay.BindingHash));
        Assert.That(first.Domains.Single().Cells, Is.EquivalentTo(Cells));
        request.Atmosphere.PressureToleranceKPa = 0.5f;
        Assert.That(KsProcgenGasBindings.Bind(Capture(request), Rooms, SharedDomain).BindingHash, Is.Not.EqualTo(first.BindingHash));
    }

    private static void AssertFailure(KsProcgenGasBindingResult result, string code)
    {
        Assert.That(result.Status, Is.EqualTo(KsProcgenGasBindingStatus.Rejected), message: result.Issue?.Code);
        Assert.That(result.Issue?.Code, Is.EqualTo(code)); Assert.That(result.Domains, Is.Empty);
        Assert.That(result.BindingHash, Is.Zero);
    }
    private static KsProcgenRequest Request() => new() { SchemaVersion = 2, RequestId = "GasBindings",
        Shape = new() { Cells = Cells.ToList() }, Utilities = new() };
    private static KsProcgenRequestSnapshot Capture(KsProcgenRequest request)
    {
        Assert.That(KsProcgenRequestSnapshot.TryCapture(request, out var snapshot, out var issue), Is.True, message: issue?.Code);
        return snapshot!;
    }
    private static void Add(KsProcgenRequest request, string id, KsProcgenUtilityScope scope, string? scopeId, KsProcgenAtmosphereSpec profile) =>
        request.Utilities!.GasAssignments.Add(new() { Id = id, Scope = scope, ScopeId = scopeId, Profile = profile });
    private static KsProcgenAtmosphereSpec Fill(bool nitrogen = false) => new() { Mode = KsProcgenAtmosphereMode.Fill,
        TemperatureKelvin = 293.15f, PressureKPa = 101.325f,
        MoleFractions = nitrogen ? new() { ["Nitrogen"] = 1f } : new() { ["Nitrogen"] = 0.79f, ["Oxygen"] = 0.21f } };
}
