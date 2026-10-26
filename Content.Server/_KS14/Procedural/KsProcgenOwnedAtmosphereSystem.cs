using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Procedural;
using Content.Shared.Atmos;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Procedural;

/// <summary>Gas work inside the existing owned-stage initialization transaction.</summary>
public sealed partial class KsProcgenOwnedAtmosphereSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private KsProcgenStageOwnershipSystem _ownership = default!;
    [Dependency] private IGameTiming _timing = default!;
    private static readonly (Vector2i Offset, AtmosDirection Direction)[] Cardinal =
        [(new(0, 1), AtmosDirection.North), (new(0, -1), AtmosDirection.South),
         (new(1, 0), AtmosDirection.East), (new(-1, 0), AtmosDirection.West)];

    internal string? InitializeOrVerify(KsProcgenOwnedStage stage, bool afterMapInitialization,
        out KsProcgenInitialAtmosphereReceipt? receipt)
    {
        receipt = null;
        if (stage.Gas == null) return null;
        if (!_ownership.Owns(stage)) return "OwnedGasStageNotOwned";
        var terrain = stage.Scene.Tiles.Select(tile => tile.Cell).ToArray();
        if (stage.InitialAtmosphere != null)
        {
            if (stage.InitialAtmosphere.BindingHash != stage.Gas.BindingHash) return "OwnedGasBindingDrift";
            var readIssue = _atmosphere.KsReadProcgenGas(stage.GridUid, terrain, out var existing);
            return readIssue ?? Validate(stage, existing, verifyMixtures: true, out _);
        }
        if (!afterMapInitialization)
            return stage.InitializationPhase == KsProcgenStageInitializationPhase.Uninitialized ? null : "OwnedGasReceiptMissing";
        var issue = _atmosphere.KsPrepareProcgenGas(stage.GridUid, terrain, out var states);
        if (issue != null) return issue;
        if (!_ownership.Owns(stage)) return "OwnedGasStageNotOwned";
        issue = Validate(stage, states, verifyMixtures: false, out var omissions);
        if (issue != null) return issue;
        var writes = new List<KsProcgenAtmosWrite>();
        var volumes = states.ToDictionary(state => state.Cell, state => state.VolumeLiters);
        foreach (var domain in stage.Gas.Domains)
        {
            if (domain.NominalProfile is not { Mode: not KsProcgenAtmosphereMode.Preserve } profile ||
                omissions.Any(omission => omission.DomainId == domain.Id && omission.Code == "PreferredGasDomainLeaks")) continue;
            if (profile.Requirement == KsProcgenAtmosphereRequirement.Preferred && domain.Cells.Any(cell =>
                    !KsProcgenAtmosphereMixturePlanner.TryPlan(profile, volumes[cell], out _, out _)))
            {
                foreach (var demand in domain.Demands.Where(demand => demand.Profile.Mode != KsProcgenAtmosphereMode.Preserve))
                    omissions.Add(new(demand.AssignmentId, domain.Id, "PreferredGasMixtureUnrepresentable") { RoomId = demand.RoomId });
                continue;
            }
            writes.AddRange(domain.Cells.Select(cell => new KsProcgenAtmosWrite(cell, profile)));
        }
        issue = _atmosphere.KsWriteProcgenGas(stage.GridUid, terrain, writes, out states);
        if (issue != null) return issue;
        if (!_ownership.Owns(stage)) return "OwnedGasStageNotOwned";
        issue = Validate(stage, states, verifyMixtures: true, out var observedOmissions);
        if (issue != null) return issue;
        receipt = new(stage.Gas.BindingHash, states, _timing.CurTick, _timing.CurTime, omissions.Concat(observedOmissions));
        return null;
    }

    private string? Validate(KsProcgenOwnedStage stage, IReadOnlyList<KsProcgenAtmosCellState> states, bool verifyMixtures,
        out List<KsProcgenGasOmission> omissions)
    {
        var binding = stage.Gas!;
        omissions = binding.Omissions.ToList();
        var actual = states.Where(state => state.OwnedMixture).ToDictionary(state => state.Cell);
        var expected = binding.Domains.SelectMany(domain => domain.Cells).ToHashSet();
        if (!expected.SetEquals(actual.Keys)) return "OwnedGasVolumeTopologyDrift";
        if (verifyMixtures && stage.Request.Utilities?.GasRestrictions is { } restrictions)
        foreach (var state in actual.Values)
        {
            if (restrictions.MinimumPressureKPa is { } minimumPressure && state.PressureKPa < minimumPressure ||
                restrictions.MaximumPressureKPa is { } maximumPressure && state.PressureKPa > maximumPressure ||
                restrictions.MinimumTemperatureKelvin is { } minimumTemperature && state.TemperatureKelvin < minimumTemperature ||
                restrictions.MaximumTemperatureKelvin is { } maximumTemperature && state.TemperatureKelvin > maximumTemperature ||
                restrictions.AllowedGases != null && state.Gases.Any(gas => gas.Moles > 0f && !restrictions.AllowedGases.Contains(gas.GasId)))
                return "OwnedGasRestrictionDrift";
        }
        foreach (var domain in binding.Domains)
        {
            foreach (var demand in domain.Demands.Where(demand => demand.Profile.Requirement == KsProcgenAtmosphereRequirement.Preferred &&
                         demand.Profile.StateRequirement == KsProcgenAtmosphereState.StableAtRest))
                omissions.Add(new(demand.AssignmentId, domain.Id, "PreferredGasStabilityUnsupported") { RoomId = demand.RoomId });
            var cells = domain.Cells.ToHashSet();
            var visited = new HashSet<Vector2i>(); var pending = new Queue<Vector2i>();
            var leaking = false;
            if (domain.Cells.Count == 0) return "OwnedGasEmptyDomain";
            visited.Add(domain.Cells[0]); pending.Enqueue(domain.Cells[0]);
            while (pending.TryDequeue(out var cell))
            {
                foreach (var (offset, direction) in Cardinal)
                {
                    if ((actual[cell].OpenDirections & direction) == 0) continue;
                    var neighbor = cell + offset;
                    // Assigned gas must have an actual resting-state boundary. Never write a hard
                    // nominal state into space or an unassigned/foreign component and certify it.
                    if (!cells.Contains(neighbor))
                    {
                        if (domain.Demands.Any(demand => demand.Profile.Requirement == KsProcgenAtmosphereRequirement.Required &&
                                demand.Profile.Mode != KsProcgenAtmosphereMode.Preserve)) return "OwnedGasDomainLeaks";
                        leaking = true;
                        continue;
                    }
                    if ((actual[neighbor].OpenDirections & direction.GetOpposite()) == 0) return "OwnedGasAsymmetricEdge";
                    if (visited.Add(neighbor)) pending.Enqueue(neighbor);
                }
            }
            if (visited.Count != cells.Count) return "OwnedGasDomainSplit";
            if (leaking)
            foreach (var demand in domain.Demands.Where(demand => demand.Profile.Mode != KsProcgenAtmosphereMode.Preserve))
                omissions.Add(new(demand.AssignmentId, domain.Id, "PreferredGasDomainLeaks") { RoomId = demand.RoomId });
            if (!verifyMixtures) continue;
            foreach (var demand in domain.Demands)
            {
                var profile = demand.Profile;
                if (profile.Mode == KsProcgenAtmosphereMode.Preserve) continue;
                if (domain.Cells.All(cell => MatchesProfile(actual[cell], profile))) continue;
                if (profile.Requirement == KsProcgenAtmosphereRequirement.Required) return "OwnedGasInitialStateDrift";
                omissions.Add(new(demand.AssignmentId, domain.Id, "PreferredGasInitialStateUnmet") { RoomId = demand.RoomId });
            }
        }
        return null;
    }

    private static bool MatchesProfile(KsProcgenAtmosCellState state, KsProcgenAtmosphereProfile profile)
    {
        if (Math.Abs((double) state.PressureKPa - (double) profile.PressureKPa!.Value) > (double) profile.PressureToleranceKPa) return false;
        if (profile.Mode == KsProcgenAtmosphereMode.Vacuum) return true;
        if (Math.Abs((double) state.TemperatureKelvin - (double) profile.TemperatureKelvin!.Value) > (double) profile.TemperatureToleranceKelvin) return false;
        var totalMoles = state.Gases.Sum(gas => (double) gas.Moles);
        if (!double.IsFinite(totalMoles) || totalMoles <= 0.0) return false;
        var requested = profile.MoleFractions.ToDictionary(gas => gas.GasId, gas => (double) gas.Fraction, StringComparer.Ordinal);
        foreach (var gasId in requested.Keys.Concat(state.Gases.Select(gas => gas.GasId)).Distinct(StringComparer.Ordinal))
        {
            var moles = state.Gases.FirstOrDefault(gas => gas.GasId == gasId)?.Moles ?? 0f;
            if (Math.Abs((double) moles / totalMoles - requested.GetValueOrDefault(gasId)) > (double) profile.FractionTolerance) return false;
        }
        return true;
    }
}
