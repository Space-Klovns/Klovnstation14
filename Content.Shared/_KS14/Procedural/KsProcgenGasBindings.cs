using System.Collections.Frozen;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Logical room ownership and an explicitly supplied gas-transfer component, not movement connectivity.</summary>
public sealed record KsProcgenGasRoomTarget(string Id, IReadOnlyList<Vector2i> Cells, KsProcgenAtmosphereProfile? ThemeProfile);
public sealed record KsProcgenGasDomainTarget(string Id, IReadOnlyList<Vector2i> Cells);
public sealed record KsProcgenGasDemand(string AssignmentId, string? RoomId, KsProcgenAtmosphereProfile Profile);
public sealed record KsProcgenGasOmission(string AssignmentId, string? DomainId, string Code)
{
    public string? RoomId { get; init; }
}

public sealed class KsProcgenBoundGasDomain
{
    internal KsProcgenBoundGasDomain(string id, IEnumerable<Vector2i> cells, IEnumerable<string> rooms,
        IEnumerable<KsProcgenGasDemand> demands)
    {
        Id = id; Cells = KsProcgenGeometry.SortCells(cells); RoomIds = rooms.OrderBy(value => value, StringComparer.Ordinal).ToReadOnlyList();
        Demands = demands.OrderBy(demand => demand.AssignmentId, StringComparer.Ordinal).ThenBy(demand => demand.RoomId, StringComparer.Ordinal).ToReadOnlyList();
        NominalProfile = Demands.OrderBy(demand => demand.Profile.Requirement).ThenBy(demand => demand.AssignmentId, StringComparer.Ordinal)
            .ThenBy(demand => demand.RoomId, StringComparer.Ordinal).FirstOrDefault()?.Profile;
    }
    public string Id { get; }
    public IReadOnlyList<Vector2i> Cells { get; }
    public IReadOnlyList<string> RoomIds { get; }
    public IReadOnlyList<KsProcgenGasDemand> Demands { get; }
    public KsProcgenAtmosphereProfile? NominalProfile { get; }
}

public enum KsProcgenGasBindingStatus : byte { Bound, InvalidInput, Rejected, BudgetExceeded }

/// <summary>Immutable target bindings. Supplied gas components and existing gas still require engine inspection.</summary>
public sealed class KsProcgenGasBindingResult
{
    internal KsProcgenGasBindingResult(KsProcgenGasBindingStatus status, KsProcgenIssue? issue,
        IEnumerable<KsProcgenBoundGasDomain> domains, IEnumerable<KsProcgenGasOmission> omissions, int work, ulong requestHash,
        string? prototypeHash = null, ulong contextHash = 0)
    {
        Status = status; Issue = issue; Domains = domains.ToReadOnlyList(); Omissions = omissions.ToReadOnlyList(); Work = work;
        RequestDeclarationHash = requestHash;
        PrototypeDeclarationHash = prototypeHash;
        ContextDeclarationHash = contextHash;
        var hash = KsProcgenStableHash.Create(); hash.AddString("ks-gas-bindings-v1");
        hash.AddInt(unchecked((int) requestHash)); hash.AddInt(unchecked((int) (requestHash >> 32)));
        hash.AddString(prototypeHash ?? "");
        hash.AddInt(unchecked((int) contextHash)); hash.AddInt(unchecked((int) (contextHash >> 32)));
        hash.AddInt(Domains.Count);
        foreach (var domain in Domains)
        {
            hash.AddString(domain.Id); hash.AddInt(domain.Cells.Count);
            foreach (var cell in domain.Cells) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
            hash.AddInt(domain.RoomIds.Count); foreach (var room in domain.RoomIds) hash.AddString(room);
            hash.AddInt(domain.Demands.Count);
            foreach (var demand in domain.Demands)
            { hash.AddString(demand.AssignmentId); hash.AddString(demand.RoomId ?? ""); demand.Profile.AddHash(ref hash); }
        }
        hash.AddInt(Omissions.Count);
        foreach (var omission in Omissions)
        { hash.AddString(omission.AssignmentId); hash.AddString(omission.DomainId ?? ""); hash.AddString(omission.RoomId ?? ""); hash.AddString(omission.Code); }
        BindingHash = status == KsProcgenGasBindingStatus.Bound ? hash.Value : 0;
    }
    public KsProcgenGasBindingStatus Status { get; }
    public KsProcgenIssue? Issue { get; }
    public IReadOnlyList<KsProcgenBoundGasDomain> Domains { get; }
    public IReadOnlyList<KsProcgenGasOmission> Omissions { get; }
    public int Work { get; }
    public ulong BindingHash { get; }
    public ulong RequestDeclarationHash { get; }
    public string? PrototypeDeclarationHash { get; }
    public ulong ContextDeclarationHash { get; }
    public bool GasClosureVerified => false;
    public bool InitialGasVerified => false;
}

public static class KsProcgenGasBindings
{
    public static KsProcgenGasBindingResult Bind(KsProcgenRequestSnapshot request,
        IReadOnlyList<KsProcgenGasRoomTarget> rooms, IReadOnlyList<KsProcgenGasDomainTarget> domains,
        int maximumWork = 262_144, KsProcgenAtmosphereProfile? themeProfile = null)
    {
        var work = 0;
        KsProcgenGasBindingResult Fail(KsProcgenGasBindingStatus status, string code, string? details = null) =>
            new(status, new(code, details ?? "Gas assignments could not bind to the complete candidate."), [], [], work, request?.DeclarationHash ?? 0);
        if (request == null || rooms == null || domains == null || rooms.Count > 256 || domains.Count > 4096 || maximumWork is < 0 or > 1_000_000)
            return Fail(KsProcgenGasBindingStatus.InvalidInput, "InvalidGasBindingInput");
        bool Spend()
        {
            if (work >= maximumWork) return false;
            work++;
            return true;
        }
        var editable = request.CopyRequest();
        if (!KsProcgenGeometry.TryNormalize(editable, out var shape, out _)) return Fail(KsProcgenGasBindingStatus.InvalidInput, "InvalidGasBindingShape");
        var roomIds = new HashSet<string>(StringComparer.Ordinal); var domainIds = new HashSet<string>(StringComparer.Ordinal);
        var roomByCell = new Dictionary<Vector2i, string>(); var gasCells = new HashSet<Vector2i>();
        foreach (var room in rooms)
        {
            if (room == null || !Id(room.Id) || room.Cells == null || room.Cells.Count == 0 || !roomIds.Add(room.Id)) return Fail(KsProcgenGasBindingStatus.InvalidInput, "InvalidGasRoomTarget");
            foreach (var cell in room.Cells)
            {
                if (!Spend()) return Fail(KsProcgenGasBindingStatus.BudgetExceeded, "GasBindingWorkBudget");
                if (!shape!.TargetCells.Contains(cell) || !roomByCell.TryAdd(cell, room.Id)) return Fail(KsProcgenGasBindingStatus.InvalidInput, "OverlappingGasRoomTargets");
            }
        }
        foreach (var domain in domains)
        {
            if (domain == null || !Id(domain.Id) || domain.Cells == null || domain.Cells.Count == 0 || !domainIds.Add(domain.Id))
                return Fail(KsProcgenGasBindingStatus.InvalidInput, "InvalidGasDomainTarget");
            foreach (var cell in domain.Cells)
            {
                if (!Spend()) return Fail(KsProcgenGasBindingStatus.BudgetExceeded, "GasBindingWorkBudget");
                if (!shape!.TargetCells.Contains(cell) && !shape.EnvelopeCells.Contains(cell) || !gasCells.Add(cell))
                    return Fail(KsProcgenGasBindingStatus.InvalidInput, "OverlappingGasDomainTargets");
            }
        }
        var assignments = request.Utilities?.GasAssignments ?? [];
        foreach (var assignment in assignments)
        {
            if (!Spend()) return Fail(KsProcgenGasBindingStatus.BudgetExceeded, "GasBindingWorkBudget");
            if (assignment.Scope == KsProcgenUtilityScope.Room && !roomIds.Contains(assignment.ScopeId!) ||
                assignment.Scope == KsProcgenUtilityScope.GasDomain && !domainIds.Contains(assignment.ScopeId!))
                return Fail(KsProcgenGasBindingStatus.Rejected, "UnknownGasAssignmentTarget",
                    details: $"Assignment '{assignment.Id}' targets missing {assignment.Scope} '{assignment.ScopeId}'.");
        }
        var roomLookup = rooms.ToDictionary(room => room.Id, StringComparer.Ordinal);
        var writable = request.Utilities?.GasWriteCells?.ToFrozenSet();
        var bound = new List<KsProcgenBoundGasDomain>(); var omissions = new List<KsProcgenGasOmission>();
        var visitedRooms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var domain in domains.OrderBy(domain => domain.Id, StringComparer.Ordinal))
        {
            var memberRooms = domain.Cells.Where(roomByCell.ContainsKey).Select(cell => roomByCell[cell]).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var demands = new List<KsProcgenGasDemand>(); var unassigned = false;
            foreach (var roomId in memberRooms.Length == 0 ? new string?[] { null } : memberRooms.Select(id => (string?) id))
            {
                if (!Spend()) return Fail(KsProcgenGasBindingStatus.BudgetExceeded, "GasBindingWorkBudget");
                if (roomId != null) visitedRooms.Add(roomId);
                var candidates = assignments.Where(assignment => assignment.Scope is KsProcgenUtilityScope.Request or KsProcgenUtilityScope.Area ||
                    assignment.Scope == KsProcgenUtilityScope.Room && assignment.ScopeId == roomId ||
                    assignment.Scope == KsProcgenUtilityScope.GasDomain && assignment.ScopeId == domain.Id).ToArray();
                var priority = candidates.Length == 0 ? -1 : candidates.Max(assignment => Priority(assignment.Scope));
                var selected = candidates.Where(assignment => Priority(assignment.Scope) == priority).ToArray();
                if (selected.Length != 0)
                {
                    if (selected.Skip(1).Any(assignment => !KsProcgenUtilityContracts.SameGasContract(assignment.Profile, selected[0].Profile)))
                        return Fail(KsProcgenGasBindingStatus.Rejected, "ConflictingBoundGasAssignments",
                            details: $"Domain '{domain.Id}', room '{roomId}': equal-priority assignments {string.Join(", ", selected.Select(assignment => assignment.Id))} disagree.");
                    demands.AddRange(selected.Select(assignment => new KsProcgenGasDemand(assignment.Id, roomId, assignment.Profile)));
                }
                // Author IDs are printable ASCII. Reserved control prefixes cannot alias author provenance.
                else if (request.Atmosphere is { } global) demands.Add(new("\u0001request", roomId, global));
                else if ((roomId == null ? themeProfile : roomLookup[roomId].ThemeProfile ?? themeProfile) is { } theme)
                    demands.Add(new("\u0001theme", roomId, theme));
                else unassigned = true;
            }
            if (demands.Count == 0 && HasStateRestrictions(request.Utilities?.GasRestrictions))
                return Fail(KsProcgenGasBindingStatus.Rejected, "UnspecifiedGasRestrictionUnverified");
            foreach (var demand in demands)
            {
                if (!Spend()) return Fail(KsProcgenGasBindingStatus.BudgetExceeded, "GasBindingWorkBudget");
                if (!MeetsRestrictions(demand.Profile, request.Utilities?.GasRestrictions))
                    return Fail(KsProcgenGasBindingStatus.Rejected, "GasAssignmentViolatesRestriction");
            }
            var required = demands.Where(demand => demand.Profile.Requirement == KsProcgenAtmosphereRequirement.Required).ToArray();
            var reference = required.FirstOrDefault() ?? demands.FirstOrDefault();
            if (reference != null && (unassigned && reference.Profile.Mode != KsProcgenAtmosphereMode.Preserve ||
                    required.Any(demand => !SameState(demand.Profile, reference.Profile))))
                return Fail(KsProcgenGasBindingStatus.Rejected, "ConflictingGasDomainStates",
                    details: $"Domain '{domain.Id}' cannot share required states from {string.Join(", ", required.Select(demand => demand.AssignmentId))} or mutate an unassigned room.");
            if (reference != null)
            {
                foreach (var incompatible in demands.Where(demand => !SameState(demand.Profile, reference.Profile)).ToArray())
                {
                    omissions.Add(new(incompatible.AssignmentId, domain.Id, "PreferredGasDomainConflict") { RoomId = incompatible.RoomId }); demands.Remove(incompatible);
                }
                if (reference.Profile.Mode != KsProcgenAtmosphereMode.Preserve)
                foreach (var cell in domain.Cells)
                {
                    if (!Spend()) return Fail(KsProcgenGasBindingStatus.BudgetExceeded, "GasBindingWorkBudget");
                    if (!shape!.CanWrite(cell) || writable != null && !writable.Contains(cell))
                        return Fail(KsProcgenGasBindingStatus.Rejected, "GasDomainWritePermissionRequired");
                }
            }
            bound.Add(new(domain.Id, domain.Cells, memberRooms, demands));
        }
        foreach (var room in rooms.Where(room => !visitedRooms.Contains(room.Id)))
        {
            if (!Spend()) return Fail(KsProcgenGasBindingStatus.BudgetExceeded, "GasBindingWorkBudget");
            var applicableAssignments = assignments.Where(assignment => assignment.Scope is KsProcgenUtilityScope.Request or KsProcgenUtilityScope.Area ||
                assignment.Scope == KsProcgenUtilityScope.Room && assignment.ScopeId == room.Id).ToArray();
            var priority = applicableAssignments.Length == 0 ? -1 : applicableAssignments.Max(assignment => Priority(assignment.Scope));
            var fallback = request.Atmosphere ?? room.ThemeProfile ?? themeProfile;
            var applicable = applicableAssignments.Length == 0
                ? fallback == null ? Array.Empty<KsProcgenGasDemand>() : new[] { new KsProcgenGasDemand(request.Atmosphere != null ? "\u0001request" : "\u0001theme", room.Id, fallback) }
                : applicableAssignments.Where(assignment => Priority(assignment.Scope) == priority)
                    .Select(assignment => new KsProcgenGasDemand(assignment.Id, room.Id, assignment.Profile)).ToArray();
            if (applicable.Any(demand => demand.Profile.Mode != KsProcgenAtmosphereMode.Preserve && demand.Profile.Requirement == KsProcgenAtmosphereRequirement.Required))
                return Fail(KsProcgenGasBindingStatus.Rejected, "GasRoomHasNoVolume");
            foreach (var demand in applicable.Where(demand => demand.Profile.Mode != KsProcgenAtmosphereMode.Preserve))
                omissions.Add(new(demand.AssignmentId, null, "PreferredGasRoomHasNoVolume") { RoomId = room.Id });
        }
        if (domains.Count == 0 && rooms.Count == 0 && (request.Atmosphere is { Mode: not KsProcgenAtmosphereMode.Preserve,
                Requirement: KsProcgenAtmosphereRequirement.Required } || assignments.Any(assignment =>
                assignment.Profile.Mode != KsProcgenAtmosphereMode.Preserve && assignment.Profile.Requirement == KsProcgenAtmosphereRequirement.Required)))
            return Fail(KsProcgenGasBindingStatus.Rejected, "GasRequestHasNoVolume");
        return new(KsProcgenGasBindingStatus.Bound, null, bound, omissions.OrderBy(omission => omission.AssignmentId, StringComparer.Ordinal)
            .ThenBy(omission => omission.DomainId, StringComparer.Ordinal).ThenBy(omission => omission.RoomId, StringComparer.Ordinal), work, request.DeclarationHash);
    }

    private static bool Id(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 512 && value.All(character => character is >= '!' and <= '~');
    private static int Priority(KsProcgenUtilityScope scope) => scope is KsProcgenUtilityScope.Room or KsProcgenUtilityScope.GasDomain ? 3 : scope == KsProcgenUtilityScope.Area ? 2 : 1;
    private static bool SameState(KsProcgenAtmosphereProfile first, KsProcgenAtmosphereProfile second) => first.Mode == second.Mode &&
        first.PressureKPa == second.PressureKPa && first.TemperatureKelvin == second.TemperatureKelvin && first.MoleFractions.SequenceEqual(second.MoleFractions);
    private static bool HasStateRestrictions(KsProcgenGasRestrictions? restrictions) => restrictions != null &&
        (restrictions.AllowedModes != null || restrictions.AllowedGases != null || restrictions.MinimumPressureKPa.HasValue || restrictions.MaximumPressureKPa.HasValue ||
         restrictions.MinimumTemperatureKelvin.HasValue || restrictions.MaximumTemperatureKelvin.HasValue || restrictions.MinimumObservationTicks.HasValue);
    private static bool MeetsRestrictions(KsProcgenAtmosphereProfile profile, KsProcgenGasRestrictions? restrictions)
    {
        if (restrictions == null) return true;
        if (restrictions.AllowedModes != null && !restrictions.AllowedModes.Contains(profile.Mode) ||
            restrictions.AllowedGases != null && profile.MoleFractions.Any(gas => !restrictions.AllowedGases.Contains(gas.GasId)) ||
            restrictions.MinimumObservationTicks is { } ticks && (profile.StateRequirement != KsProcgenAtmosphereState.StableAtRest || profile.ObservationTicks < ticks)) return false;
        if (profile.Mode == KsProcgenAtmosphereMode.Preserve) return restrictions.MinimumPressureKPa == null && restrictions.MaximumPressureKPa == null &&
            restrictions.MinimumTemperatureKelvin == null && restrictions.MaximumTemperatureKelvin == null && restrictions.AllowedGases == null;
        return (!restrictions.MinimumPressureKPa.HasValue || profile.PressureKPa >= restrictions.MinimumPressureKPa) &&
            (!restrictions.MaximumPressureKPa.HasValue || profile.PressureKPa <= restrictions.MaximumPressureKPa) &&
            (!restrictions.MinimumTemperatureKelvin.HasValue || profile.TemperatureKelvin >= restrictions.MinimumTemperatureKelvin) &&
            (!restrictions.MaximumTemperatureKelvin.HasValue || profile.TemperatureKelvin <= restrictions.MaximumTemperatureKelvin);
    }
}
