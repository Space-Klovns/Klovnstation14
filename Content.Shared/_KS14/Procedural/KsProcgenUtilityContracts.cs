using System.Linq;
using Content.Shared.Atmos;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenUtilityScope : byte { Request, Area, Room, GasDomain }
public enum KsProcgenConsumerSelector : byte { AllPowered, Prototype, Member }
public enum KsProcgenUtilityRequirement : byte { Required, Preferred }

[DataDefinition]
public sealed partial class KsProcgenGasAssignmentSpec
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField] public KsProcgenUtilityScope Scope;
    [DataField] public string? ScopeId;
    [DataField] public string? ProfileId;
    [DataField] public KsProcgenAtmosphereSpec? Profile;
}

/// <summary>Planned entity selectors use stable authoring IDs, never live entity UIDs.</summary>
[DataDefinition]
public sealed partial class KsProcgenElectricalConsumerSpec
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField] public KsProcgenUtilityScope Scope;
    [DataField] public string? ScopeId;
    [DataField] public KsProcgenConsumerSelector Selector;
    [DataField] public string? SelectorId;
    [DataField] public string CircuitId = "Main";
    [DataField] public KsProcgenUtilityRequirement Requirement = KsProcgenUtilityRequirement.Required;
    [DataField] public KsProcgenElectricalSupply Supply = KsProcgenElectricalSupply.Operational;
    /// <summary>Null requires prototype/load inspection; it never means a zero-watt consumer.</summary>
    [DataField] public float? NominalWatts;
    [DataField] public float? PeakWatts;
    [DataField] public int MinimumMatches = 1;
}

[DataDefinition]
public sealed partial class KsProcgenGasRestrictionsSpec
{
    [DataField] public List<KsProcgenAtmosphereMode>? AllowedModes;
    [DataField] public List<string>? AllowedGases;
    [DataField] public float? MinimumPressureKPa;
    [DataField] public float? MaximumPressureKPa;
    [DataField] public float? MinimumTemperatureKelvin;
    [DataField] public float? MaximumTemperatureKelvin;
    [DataField] public int? MinimumObservationTicks;
}

[DataDefinition]
public sealed partial class KsProcgenElectricalRestrictionsSpec
{
    [DataField] public bool RequireOperationalSupply;
    [DataField] public bool ForbidBatteryOnly;
}

/// <summary>
/// Versioned utility intent. Optional masks narrow request write permission; they never grant it.
/// Null inherits the writable mask, while an empty mask permits no writes in that layer.
/// </summary>
[DataDefinition]
public sealed partial class KsProcgenUtilityContractSpec
{
    [DataField] public int SchemaVersion = 1;
    [DataField] public List<KsProcgenGasAssignmentSpec> GasAssignments = new();
    [DataField] public List<KsProcgenElectricalConsumerSpec> Consumers = new();
    [DataField] public List<Vector2i>? GasWriteCells;
    [DataField] public List<Vector2i>? CableWriteCells;
    [DataField] public List<Vector2i>? DeviceWriteCells;
    [DataField] public KsProcgenGasRestrictionsSpec GasRestrictions = new();
    [DataField] public KsProcgenElectricalRestrictionsSpec ElectricalRestrictions = new();
}

public sealed record KsProcgenScopedGas(string Id, KsProcgenUtilityScope Scope, string? ScopeId, KsProcgenAtmosphereProfile Profile);
public sealed record KsProcgenElectricalConsumer(string Id, KsProcgenUtilityScope Scope, string? ScopeId,
    KsProcgenConsumerSelector Selector, string? SelectorId, string CircuitId, KsProcgenUtilityRequirement Requirement,
    KsProcgenElectricalSupply Supply, float? NominalWatts, float? PeakWatts, int MinimumMatches);
public sealed record KsProcgenGasRestrictions(IReadOnlyList<KsProcgenAtmosphereMode>? AllowedModes, IReadOnlyList<string>? AllowedGases,
    float? MinimumPressureKPa, float? MaximumPressureKPa, float? MinimumTemperatureKelvin, float? MaximumTemperatureKelvin,
    int? MinimumObservationTicks);
public sealed record KsProcgenElectricalRestrictions(bool RequireOperationalSupply, bool ForbidBatteryOnly);

/// <summary>Immutable intent, not gas isolation, receiver membership, inspected watts or permission evidence.</summary>
public sealed class KsProcgenUtilityContract
{
    internal KsProcgenUtilityContract(KsProcgenUtilityContractSpec spec, string areaId, IEnumerable<KsProcgenScopedGas> gases)
    {
        GasAssignments = gases.OrderBy(gas => gas.Id, StringComparer.Ordinal).ToReadOnlyList();
        Consumers = spec.Consumers.OrderBy(consumer => consumer.Id, StringComparer.Ordinal).Select(consumer => new KsProcgenElectricalConsumer(
            consumer.Id, consumer.Scope, KsProcgenUtilityContracts.ScopeId(consumer.Scope, consumer.ScopeId, areaId), consumer.Selector,
            consumer.SelectorId, consumer.CircuitId, consumer.Requirement, consumer.Supply, Zero(consumer.NominalWatts), Zero(consumer.PeakWatts), consumer.MinimumMatches)).ToReadOnlyList();
        GasWriteCells = Cells(spec.GasWriteCells); CableWriteCells = Cells(spec.CableWriteCells); DeviceWriteCells = Cells(spec.DeviceWriteCells);
        var gas = spec.GasRestrictions;
        GasRestrictions = new(gas.AllowedModes?.OrderBy(mode => mode).ToReadOnlyList(),
            gas.AllowedGases?.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToReadOnlyList(),
            Zero(gas.MinimumPressureKPa), Zero(gas.MaximumPressureKPa), Zero(gas.MinimumTemperatureKelvin), Zero(gas.MaximumTemperatureKelvin), gas.MinimumObservationTicks);
        ElectricalRestrictions = new(spec.ElectricalRestrictions.RequireOperationalSupply, spec.ElectricalRestrictions.ForbidBatteryOnly);
    }

    public int SchemaVersion => 1;
    public IReadOnlyList<KsProcgenScopedGas> GasAssignments { get; }
    public IReadOnlyList<KsProcgenElectricalConsumer> Consumers { get; }
    public IReadOnlyList<Vector2i>? GasWriteCells { get; }
    public IReadOnlyList<Vector2i>? CableWriteCells { get; }
    public IReadOnlyList<Vector2i>? DeviceWriteCells { get; }
    public KsProcgenGasRestrictions GasRestrictions { get; }
    public KsProcgenElectricalRestrictions ElectricalRestrictions { get; }
    public bool RequiresTargetBinding => GasAssignments.Any(gas => gas.Scope is KsProcgenUtilityScope.Room or KsProcgenUtilityScope.GasDomain) || Consumers.Count > 0;
    public bool HasRequirements => GasAssignments.Count > 0 || Consumers.Count > 0 || GasWriteCells != null || CableWriteCells != null ||
        DeviceWriteCells != null || GasRestrictions.AllowedModes != null || GasRestrictions.AllowedGases != null ||
        GasRestrictions.MinimumPressureKPa.HasValue || GasRestrictions.MaximumPressureKPa.HasValue || GasRestrictions.MinimumTemperatureKelvin.HasValue ||
        GasRestrictions.MaximumTemperatureKelvin.HasValue || GasRestrictions.MinimumObservationTicks.HasValue ||
        ElectricalRestrictions.RequireOperationalSupply || ElectricalRestrictions.ForbidBatteryOnly;

    public KsProcgenUtilityContractSpec CopySpec() => new()
    {
        GasAssignments = GasAssignments.Select(gas => new KsProcgenGasAssignmentSpec
            { Id = gas.Id, Scope = gas.Scope, ScopeId = gas.ScopeId, ProfileId = gas.Profile.SourceProfileId, Profile = gas.Profile.CopySpec() }).ToList(),
        Consumers = Consumers.Select(consumer => new KsProcgenElectricalConsumerSpec
        {
            Id = consumer.Id, Scope = consumer.Scope, ScopeId = consumer.ScopeId, Selector = consumer.Selector, SelectorId = consumer.SelectorId,
            CircuitId = consumer.CircuitId, Requirement = consumer.Requirement, Supply = consumer.Supply,
            NominalWatts = consumer.NominalWatts, PeakWatts = consumer.PeakWatts, MinimumMatches = consumer.MinimumMatches,
        }).ToList(),
        GasWriteCells = GasWriteCells?.ToList(), CableWriteCells = CableWriteCells?.ToList(), DeviceWriteCells = DeviceWriteCells?.ToList(),
        GasRestrictions = new()
        {
            AllowedModes = GasRestrictions.AllowedModes?.ToList(), AllowedGases = GasRestrictions.AllowedGases?.ToList(),
            MinimumPressureKPa = GasRestrictions.MinimumPressureKPa, MaximumPressureKPa = GasRestrictions.MaximumPressureKPa,
            MinimumTemperatureKelvin = GasRestrictions.MinimumTemperatureKelvin, MaximumTemperatureKelvin = GasRestrictions.MaximumTemperatureKelvin,
            MinimumObservationTicks = GasRestrictions.MinimumObservationTicks,
        },
        ElectricalRestrictions = new() { RequireOperationalSupply = ElectricalRestrictions.RequireOperationalSupply, ForbidBatteryOnly = ElectricalRestrictions.ForbidBatteryOnly },
    };

    internal void AddHash(ref KsProcgenStableHash hash)
    {
        hash.AddString("ks-procgen-utility-contract-v1"); hash.AddInt(SchemaVersion);
        hash.AddInt(GasAssignments.Count);
        foreach (var gas in GasAssignments)
        {
            hash.AddString(gas.Id); hash.AddInt((int) gas.Scope); AddString(ref hash, gas.ScopeId); gas.Profile.AddHash(ref hash);
        }
        hash.AddInt(Consumers.Count);
        foreach (var consumer in Consumers)
        {
            hash.AddString(consumer.Id); hash.AddInt((int) consumer.Scope); AddString(ref hash, consumer.ScopeId);
            hash.AddInt((int) consumer.Selector); AddString(ref hash, consumer.SelectorId); hash.AddString(consumer.CircuitId);
            hash.AddInt((int) consumer.Requirement); hash.AddInt((int) consumer.Supply);
            AddFloat(ref hash, consumer.NominalWatts); AddFloat(ref hash, consumer.PeakWatts); hash.AddInt(consumer.MinimumMatches);
        }
        AddCells(ref hash, GasWriteCells); AddCells(ref hash, CableWriteCells); AddCells(ref hash, DeviceWriteCells);
        hash.AddInt(GasRestrictions.AllowedModes == null ? -1 : GasRestrictions.AllowedModes.Count);
        if (GasRestrictions.AllowedModes != null) foreach (var mode in GasRestrictions.AllowedModes) hash.AddInt((int) mode);
        hash.AddInt(GasRestrictions.AllowedGases == null ? -1 : GasRestrictions.AllowedGases.Count);
        if (GasRestrictions.AllowedGases != null) foreach (var gas in GasRestrictions.AllowedGases) hash.AddString(gas);
        AddFloat(ref hash, GasRestrictions.MinimumPressureKPa); AddFloat(ref hash, GasRestrictions.MaximumPressureKPa);
        AddFloat(ref hash, GasRestrictions.MinimumTemperatureKelvin); AddFloat(ref hash, GasRestrictions.MaximumTemperatureKelvin);
        hash.AddInt(GasRestrictions.MinimumObservationTicks ?? -1);
        hash.AddInt(ElectricalRestrictions.RequireOperationalSupply ? 1 : 0); hash.AddInt(ElectricalRestrictions.ForbidBatteryOnly ? 1 : 0);
    }

    public ulong DeclarationHash { get { var hash = KsProcgenStableHash.Create(); AddHash(ref hash); return hash.Value; } }
    private static float? Zero(float? value) => value == 0f ? 0f : value;
    private static IReadOnlyList<Vector2i>? Cells(IEnumerable<Vector2i>? cells) => cells == null ? null : KsProcgenGeometry.SortCells(cells.Distinct()).ToReadOnlyList();
    private static void AddString(ref KsProcgenStableHash hash, string? value) { hash.AddInt(value == null ? 0 : 1); if (value != null) hash.AddString(value); }
    private static void AddFloat(ref KsProcgenStableHash hash, float? value) { hash.AddInt(value.HasValue ? 1 : 0); if (value.HasValue) hash.AddInt(BitConverter.SingleToInt32Bits(value.Value)); }
    private static void AddCells(ref KsProcgenStableHash hash, IReadOnlyList<Vector2i>? cells)
    {
        hash.AddInt(cells == null ? -1 : cells.Count);
        if (cells != null) foreach (var cell in cells) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
    }
}

public static class KsProcgenUtilityContracts
{
    public const int MaximumAssignments = 128;
    public const int MaximumConsumers = 1024;

    internal static string? ScopeId(KsProcgenUtilityScope scope, string? id, string areaId) => scope == KsProcgenUtilityScope.Area ? id ?? areaId : id;

    internal static bool Validate(KsProcgenUtilityContractSpec spec, string areaId, KsProcgenNormalizedShape shape, int maximumCells,
        out KsProcgenIssue? issue)
    {
        issue = null;
        if (!KsProcgenAreaNormalizer.ValidId(areaId) || shape == null || maximumCells is < 1 or > 65_536)
        { issue = new("InvalidUtilityContext", "A valid area ID, normalized shape and bounded cell limit are required."); return false; }
        if (spec == null || spec.SchemaVersion != 1)
        { issue = new("UnsupportedUtilityContractSchema", "Utility contract schema version 1 is required."); return false; }
        if (spec.GasAssignments == null || spec.Consumers == null || spec.GasRestrictions == null || spec.ElectricalRestrictions == null ||
            spec.GasAssignments.Count > MaximumAssignments || spec.Consumers.Count > MaximumConsumers)
        { issue = new("InvalidUtilityContract", "Utility assignments and consumers must be nonnull bounded collections."); return false; }
        var gasIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var gas in spec.GasAssignments)
        {
            if (gas == null || !KsProcgenAreaNormalizer.ValidId(gas.Id) || !gasIds.Add(gas.Id) || !ValidScope(gas.Scope, gas.ScopeId, areaId) ||
                gas.ProfileId == null && gas.Profile == null || gas.ProfileId != null && !KsProcgenAreaNormalizer.ValidId(gas.ProfileId))
            { issue = new("InvalidGasAssignment", "Gas assignments need unique IDs, a valid scope and an explicit profile."); return false; }
            if (gas.Profile != null && !KsProcgenAtmosphereProfiles.TrySnapshot(gas.Profile, out _, out issue)) return false;
        }
        var consumerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var consumer in spec.Consumers)
        {
            if (consumer == null || !KsProcgenAreaNormalizer.ValidId(consumer.Id) || !consumerIds.Add(consumer.Id) || !ValidScope(consumer.Scope, consumer.ScopeId, areaId) ||
                !Enum.IsDefined(consumer.Selector) || !Enum.IsDefined(consumer.Requirement) || !Enum.IsDefined(consumer.Supply) ||
                (consumer.Selector == KsProcgenConsumerSelector.AllPowered ? consumer.SelectorId != null : !ValidTarget(consumer.SelectorId)) ||
                !KsProcgenAreaNormalizer.ValidId(consumer.CircuitId) || !Nonnegative(consumer.NominalWatts) || !Nonnegative(consumer.PeakWatts) ||
                consumer.PeakWatts.HasValue && consumer.NominalWatts.HasValue && consumer.PeakWatts < consumer.NominalWatts || consumer.MinimumMatches is < 0 or > 4096)
            { issue = new("InvalidElectricalConsumer", "Consumers need unique IDs, stable selectors, valid circuits and finite ordered load declarations."); return false; }
        }
        foreach (var mask in new[] { spec.GasWriteCells, spec.CableWriteCells, spec.DeviceWriteCells })
            if (mask != null && (mask.Count > maximumCells || mask.Any(cell => !shape.CanWrite(cell))))
            { issue = new("UtilityOwnershipOutsideWritable", "Utility layer masks may only narrow the request's writable cells."); return false; }
        var restrictions = spec.GasRestrictions;
        if (restrictions.AllowedModes != null && (restrictions.AllowedModes.Count is < 1 or > 3 || restrictions.AllowedModes.Any(mode => !Enum.IsDefined(mode)) ||
                restrictions.AllowedModes.Distinct().Count() != restrictions.AllowedModes.Count) ||
            restrictions.AllowedGases != null && (restrictions.AllowedGases.Count > Atmospherics.TotalNumberOfGases || restrictions.AllowedGases.Any(id => !KsProcgenAtmosphereProfiles.TryGas(id, out _))) ||
            !Range(restrictions.MinimumPressureKPa, restrictions.MaximumPressureKPa) || !Temperature(restrictions.MinimumTemperatureKelvin) || !Temperature(restrictions.MaximumTemperatureKelvin) ||
            restrictions.MinimumTemperatureKelvin.HasValue && restrictions.MaximumTemperatureKelvin.HasValue && restrictions.MinimumTemperatureKelvin > restrictions.MaximumTemperatureKelvin ||
            restrictions.MinimumObservationTicks is < 1 or > KsProcgenAtmosphereProfiles.MaximumObservationTicks)
        { issue = new("InvalidGasRestrictions", "Gas restrictions need supported modes/gases and finite ordered state bounds."); return false; }
        return true;
    }

    public static bool TryCapture(KsProcgenUtilityContractSpec spec, string areaId, KsProcgenNormalizedShape shape, int maximumCells,
        out KsProcgenUtilityContract? contract, out KsProcgenIssue? issue, IPrototypeManager? manager = null, KsProcgenUtilityDraft? draft = null)
    {
        contract = null;
        if (!Validate(spec, areaId, shape, maximumCells, out issue)) return false;
        var gases = new List<KsProcgenScopedGas>();
        foreach (var gas in spec.GasAssignments)
        {
            KsProcgenAtmosphereProfile? profile;
            if (manager != null)
            {
                if (!KsProcgenAtmosphereProfiles.TryResolveSelection(manager, gas.ProfileId, gas.Profile, null, out profile, out issue, draft: draft)) return false;
                // Preserve reference provenance even when a portable inline override is used.
                if (!KsProcgenAtmosphereProfiles.TrySnapshot(profile!.CopySpec(), out profile, out issue, sourceProfileId: gas.ProfileId)) return false;
            }
            else
            {
                if (gas.Profile == null)
                { issue = new("UnresolvedScopedGasReference", "Scoped gas names must be resolved before headless capture."); return false; }
                if (!KsProcgenAtmosphereProfiles.TrySnapshot(gas.Profile, out profile, out issue, sourceProfileId: gas.ProfileId)) return false;
            }
            gases.Add(new(gas.Id, gas.Scope, ScopeId(gas.Scope, gas.ScopeId, areaId), profile!));
        }
        foreach (var slot in gases.GroupBy(gas => (gas.Scope, gas.ScopeId)))
        {
            var assignments = slot.OrderBy(gas => gas.Id, StringComparer.Ordinal).ToArray();
            if (assignments.Skip(1).Any(gas => !SameGasContract(gas.Profile, assignments[0].Profile)))
            { issue = new("ConflictingScopedGasAssignments", $"Equal-specificity gas assignments conflict: {string.Join(", ", assignments.Select(gas => gas.Id))}."); return false; }
        }
        if (manager != null && spec.Consumers.Any(consumer => consumer.Selector == KsProcgenConsumerSelector.Prototype &&
                !manager.HasIndex<EntityPrototype>(consumer.SelectorId!)))
        { issue = new("UnknownConsumerPrototype", "A consumer prototype selector is not present in the receiving content."); return false; }
        contract = new(spec, areaId, gases);
        return true;
    }

    // Conflict decisions compare values rather than relying on a noncryptographic hash.
    internal static bool SameGasContract(KsProcgenAtmosphereProfile left, KsProcgenAtmosphereProfile right) =>
        left.Mode == right.Mode && left.TemperatureKelvin == right.TemperatureKelvin && left.PressureKPa == right.PressureKPa &&
        left.Requirement == right.Requirement && left.StateRequirement == right.StateRequirement && left.ObservationTicks == right.ObservationTicks &&
        left.PressureToleranceKPa == right.PressureToleranceKPa && left.TemperatureToleranceKelvin == right.TemperatureToleranceKelvin &&
        left.FractionTolerance == right.FractionTolerance && left.MoleFractions.SequenceEqual(right.MoleFractions);

    /// <summary>Declarations are transportable; the current composed planners cannot realize them yet.</summary>
    internal static bool CheckPlanningSupport(KsProcgenRequest request, out KsProcgenIssue? issue)
    {
        issue = null;
        if (request.Utilities == null) return true;
        if (!KsProcgenGeometry.TryNormalize(request, out _, out issue)) return false;
        if (!HasDeclarations(request.Utilities)) return true;
        issue = new("UtilityPlanningUnsupported", "Scoped utility requirements need integrated candidate binding and utility planning.");
        return false;
    }
    internal static bool HasDeclarations(KsProcgenUtilityContractSpec spec) => spec.GasAssignments.Count > 0 || spec.Consumers.Count > 0 ||
        spec.GasWriteCells != null || spec.CableWriteCells != null || spec.DeviceWriteCells != null || spec.GasRestrictions.AllowedModes != null ||
        spec.GasRestrictions.AllowedGases != null || spec.GasRestrictions.MinimumPressureKPa.HasValue || spec.GasRestrictions.MaximumPressureKPa.HasValue ||
        spec.GasRestrictions.MinimumTemperatureKelvin.HasValue || spec.GasRestrictions.MaximumTemperatureKelvin.HasValue || spec.GasRestrictions.MinimumObservationTicks.HasValue ||
        spec.ElectricalRestrictions.RequireOperationalSupply || spec.ElectricalRestrictions.ForbidBatteryOnly;
    private static bool ValidScope(KsProcgenUtilityScope scope, string? id, string areaId) => Enum.IsDefined(scope) &&
        (scope == KsProcgenUtilityScope.Request ? id == null : scope == KsProcgenUtilityScope.Area ? id == null || id == areaId : ValidTarget(id));
    private static bool ValidTarget(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 512 && id.All(character => character is >= '!' and <= '~');
    private static bool Nonnegative(float? value) => !value.HasValue || float.IsFinite(value.Value) && value >= 0f;
    private static bool Range(float? minimum, float? maximum) => Nonnegative(minimum) && Nonnegative(maximum) && (!minimum.HasValue || !maximum.HasValue || minimum <= maximum);
    private static bool Temperature(float? value) => !value.HasValue || float.IsFinite(value.Value) && value >= Atmospherics.TCMB && value <= Atmospherics.Tmax;
}
