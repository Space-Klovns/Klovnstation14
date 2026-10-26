using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenElectricalMode : byte { Disabled, ValidateExisting, AutoDistribute }
public enum KsProcgenElectricalSupply : byte { Operational, WiredOnly }
public enum KsProcgenElectricalCircuitPolicy : byte { Shared, Independent }
public enum KsProcgenElectricalVoltage : byte { High, Medium, Low }
public enum KsProcgenElectricalConnection : byte { ReuseExisting, JoinAtCell }

[DataDefinition]
public sealed partial class KsProcgenElectricalDeviceSpec
{
    [DataField(required: true)] public string Prototype = string.Empty;
    [DataField] public float Weight = 1f;
}

/// <summary>Author declaration; this is neither an inspected power source nor write permission.</summary>
[DataDefinition]
public sealed partial class KsProcgenElectricalInterfaceSpec
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public Vector2i Cell;
    [DataField] public KsProcgenElectricalVoltage Voltage = KsProcgenElectricalVoltage.High;
    [DataField(required: true)] public string NetworkKey = string.Empty;
    [DataField] public string CircuitId = "Main";
    [DataField] public float AvailableWatts;
    [DataField] public bool Enabled = true;
    [DataField] public KsProcgenElectricalConnection Connection = KsProcgenElectricalConnection.ReuseExisting;
}

/// <summary>Versioned distribution authoring data, separate from a live power network.</summary>
[DataDefinition, Virtual]
public partial class KsProcgenElectricalSpec
{
    // Schema one remains readable for historical primitive tests. New closet authoring uses
    // schema two; switching the default awaits the joint utility-room layout rewrite.
    [DataField] public int SchemaVersion = 1;
    [DataField] public KsProcgenElectricalDistributionSpec? Distribution;
    [DataField] public KsProcgenElectricalMode Mode = KsProcgenElectricalMode.Disabled;
    [DataField] public string HighVoltageCable = "CableHV";
    [DataField] public string MediumVoltageCable = "CableMV";
    [DataField] public string LowVoltageCable = "CableApcExtension";
    [DataField] public List<KsProcgenElectricalDeviceSpec> Substations = new() { new() { Prototype = "SubstationBasic" } };
    [DataField] public List<KsProcgenElectricalDeviceSpec> Apcs = new() { new() { Prototype = "APCBasic" } };
    [DataField] public List<KsProcgenElectricalInterfaceSpec> Interfaces = new();
    [DataField] public KsProcgenElectricalCircuitPolicy CircuitPolicy = KsProcgenElectricalCircuitPolicy.Shared;
    [DataField] public List<string> CircuitIds = new() { "Main" };
    [DataField] public KsProcgenElectricalSupply SupplyRequirement = KsProcgenElectricalSupply.Operational;
    [DataField] public float DemandMargin = 0.2f;
    [DataField] public int StartupTicks = 30;
    [DataField] public int ObservationTicks = 60;
    [DataField] public bool AllowBatteryOnly;
    /// <summary>Null retains the selected prototype's starting charge; never implies infinite charge.</summary>
    [DataField] public float? InitialChargeFraction;
    [DataField] public bool ReuseExistingCables = true;
}

[Prototype]
public sealed partial class KsProcgenElectricalProfilePrototype : KsProcgenElectricalSpec, IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
}

public sealed record KsProcgenElectricalDevice(string Prototype, float Weight);
public sealed record KsProcgenElectricalInterface(string Id, Vector2i Cell, KsProcgenElectricalVoltage Voltage,
    string NetworkKey, string CircuitId, float AvailableWatts, bool Enabled, KsProcgenElectricalConnection Connection);

/// <summary>Immutable authoring snapshot. Capacity and connectivity are declarations, not verification.</summary>
public sealed class KsProcgenElectricalProfile
{
    internal KsProcgenElectricalProfile(KsProcgenElectricalSpec spec, string? sourceProfileId)
    {
        SchemaVersion = spec.SchemaVersion;
        Distribution = spec.SchemaVersion == 2 ? new(spec.Distribution ?? new()) : null;
        SourceProfileId = sourceProfileId;
        Mode = spec.Mode;
        HighVoltageCable = spec.HighVoltageCable;
        MediumVoltageCable = spec.MediumVoltageCable;
        LowVoltageCable = spec.LowVoltageCable;
        Substations = KsProcgenReadOnly.Copy(spec.Substations.OrderBy(choice => choice.Prototype, StringComparer.Ordinal)
            .Select(choice => new KsProcgenElectricalDevice(choice.Prototype, Zero(choice.Weight))));
        Apcs = KsProcgenReadOnly.Copy(spec.Apcs.OrderBy(choice => choice.Prototype, StringComparer.Ordinal)
            .Select(choice => new KsProcgenElectricalDevice(choice.Prototype, Zero(choice.Weight))));
        Interfaces = KsProcgenReadOnly.Copy(spec.Interfaces.OrderBy(port => port.Id, StringComparer.Ordinal)
            .Select(port => new KsProcgenElectricalInterface(port.Id, port.Cell, port.Voltage,
                port.NetworkKey, port.CircuitId, Zero(port.AvailableWatts), port.Enabled, port.Connection)));
        CircuitPolicy = spec.CircuitPolicy;
        CircuitIds = KsProcgenReadOnly.Copy(spec.CircuitIds.OrderBy(id => id, StringComparer.Ordinal));
        SupplyRequirement = spec.SupplyRequirement;
        DemandMargin = Zero(spec.DemandMargin);
        StartupTicks = spec.StartupTicks;
        ObservationTicks = spec.ObservationTicks;
        AllowBatteryOnly = spec.AllowBatteryOnly;
        InitialChargeFraction = spec.InitialChargeFraction.HasValue ? Zero(spec.InitialChargeFraction.Value) : null;
        ReuseExistingCables = spec.ReuseExistingCables;
    }

    public int SchemaVersion { get; }
    public KsProcgenElectricalDistribution? Distribution { get; }
    public string? SourceProfileId { get; }
    public KsProcgenElectricalMode Mode { get; }
    public string HighVoltageCable { get; }
    public string MediumVoltageCable { get; }
    public string LowVoltageCable { get; }
    public IReadOnlyList<KsProcgenElectricalDevice> Substations { get; }
    public IReadOnlyList<KsProcgenElectricalDevice> Apcs { get; }
    public IReadOnlyList<KsProcgenElectricalInterface> Interfaces { get; }
    public KsProcgenElectricalCircuitPolicy CircuitPolicy { get; }
    public IReadOnlyList<string> CircuitIds { get; }
    public KsProcgenElectricalSupply SupplyRequirement { get; }
    public float DemandMargin { get; }
    public int StartupTicks { get; }
    public int ObservationTicks { get; }
    public bool AllowBatteryOnly { get; }
    public float? InitialChargeFraction { get; }
    public bool ReuseExistingCables { get; }

    public KsProcgenElectricalSpec CopySpec() => new()
    {
        SchemaVersion = SchemaVersion, Distribution = Distribution?.CopySpec(),
        Mode = Mode, HighVoltageCable = HighVoltageCable, MediumVoltageCable = MediumVoltageCable,
        LowVoltageCable = LowVoltageCable,
        Substations = Substations.Select(choice => new KsProcgenElectricalDeviceSpec { Prototype = choice.Prototype, Weight = choice.Weight }).ToList(),
        Apcs = Apcs.Select(choice => new KsProcgenElectricalDeviceSpec { Prototype = choice.Prototype, Weight = choice.Weight }).ToList(),
        Interfaces = Interfaces.Select(port => new KsProcgenElectricalInterfaceSpec
        {
            Id = port.Id, Cell = port.Cell, Voltage = port.Voltage, NetworkKey = port.NetworkKey,
            CircuitId = port.CircuitId, AvailableWatts = port.AvailableWatts, Enabled = port.Enabled, Connection = port.Connection,
        }).ToList(),
        CircuitPolicy = CircuitPolicy, CircuitIds = CircuitIds.ToList(), SupplyRequirement = SupplyRequirement,
        DemandMargin = DemandMargin, StartupTicks = StartupTicks, ObservationTicks = ObservationTicks,
        AllowBatteryOnly = AllowBatteryOnly, InitialChargeFraction = InitialChargeFraction, ReuseExistingCables = ReuseExistingCables,
    };

    public ulong SemanticHash
    {
        get
        {
            var hash = KsProcgenStableHash.Create();
            hash.AddString(SchemaVersion == 1 ? "ks-procgen-electrical-v1" : "ks-procgen-electrical-v2");
            AddHash(ref hash);
            return hash.Value;
        }
    }

    internal void AddHash(ref KsProcgenStableHash hash)
    {
        hash.AddInt(SchemaVersion); hash.AddString(SourceProfileId ?? string.Empty); hash.AddInt((int) Mode);
        hash.AddString(HighVoltageCable); hash.AddString(MediumVoltageCable); hash.AddString(LowVoltageCable);
        AddChoices(ref hash, Substations); AddChoices(ref hash, Apcs);
        hash.AddInt(Interfaces.Count);
        foreach (var port in Interfaces)
        {
            hash.AddString(port.Id); hash.AddInt(port.Cell.X); hash.AddInt(port.Cell.Y); hash.AddInt((int) port.Voltage);
            hash.AddString(port.NetworkKey); hash.AddString(port.CircuitId);
            hash.AddInt(BitConverter.SingleToInt32Bits(port.AvailableWatts)); hash.AddInt(port.Enabled ? 1 : 0);
            hash.AddInt((int) port.Connection);
        }
        hash.AddInt((int) CircuitPolicy); hash.AddInt(CircuitIds.Count);
        foreach (var id in CircuitIds) hash.AddString(id);
        hash.AddInt((int) SupplyRequirement); hash.AddInt(BitConverter.SingleToInt32Bits(DemandMargin));
        hash.AddInt(StartupTicks); hash.AddInt(ObservationTicks); hash.AddInt(AllowBatteryOnly ? 1 : 0);
        hash.AddInt(InitialChargeFraction.HasValue ? 1 : 0);
        hash.AddInt(BitConverter.SingleToInt32Bits(InitialChargeFraction ?? 0f));
        hash.AddInt(ReuseExistingCables ? 1 : 0);
        if (Distribution != null) Distribution.AddHash(ref hash);
    }

    private static void AddChoices(ref KsProcgenStableHash hash, IReadOnlyList<KsProcgenElectricalDevice> choices)
    {
        hash.AddInt(choices.Count);
        foreach (var choice in choices)
        {
            hash.AddString(choice.Prototype); hash.AddInt(BitConverter.SingleToInt32Bits(choice.Weight));
        }
    }

    private static float Zero(float value) => value == 0f ? 0f : value;
}

public static class KsProcgenElectricalProfiles
{
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Explicit, detached migration. Schema-one hashes remain historical; upgrading adds the
    /// revised closet/coverage defaults and changes identity. It does not make the old planner
    /// capable of satisfying the new profile or authorize applying an old preview.
    /// </summary>
    public static bool TryUpgrade(KsProcgenElectricalSpec spec, out KsProcgenElectricalSpec? upgraded,
        out KsProcgenIssue? issue, Func<string, bool>? entityExists = null)
    {
        upgraded = null;
        if (!TrySnapshot(spec, out var profile, out issue, entityExists: entityExists)) return false;
        var copy = profile!.CopySpec();
        copy.SchemaVersion = CurrentSchemaVersion;
        copy.Distribution ??= new();
        if (!TrySnapshot(copy, out profile, out issue, entityExists: entityExists)) return false;
        upgraded = profile!.CopySpec();
        return true;
    }

    public static bool TrySnapshot(KsProcgenElectricalSpec spec, out KsProcgenElectricalProfile? profile,
        out KsProcgenIssue? issue, string? sourceProfileId = null, Func<string, bool>? entityExists = null)
    {
        profile = null;
        issue = null;
        if (spec == null || spec.SchemaVersion is not (1 or CurrentSchemaVersion))
        {
            issue = new("UnsupportedElectricalSchema", "Electrical schema version 1 or 2 is required.");
            return false;
        }
        if (spec.SchemaVersion == 1 && spec.Distribution != null)
        {
            issue = new("ElectricalDistributionSchemaRequired", "Closet and service-coverage declarations require electrical schema two; upgrade explicitly.");
            return false;
        }
        if (spec.SchemaVersion == CurrentSchemaVersion &&
            !KsProcgenElectricalDistribution.Validate(spec.Distribution ?? new(), out issue)) return false;
        if (sourceProfileId != null && !KsProcgenAreaNormalizer.ValidId(sourceProfileId) ||
            !Enum.IsDefined(spec.Mode) || !Enum.IsDefined(spec.CircuitPolicy) || !Enum.IsDefined(spec.SupplyRequirement) ||
            !FiniteNonnegative(spec.DemandMargin) || spec.DemandMargin > 10f || spec.StartupTicks is < 0 or > 3600 ||
            spec.ObservationTicks is < 1 or > 3600 ||
            spec.InitialChargeFraction is { } charge && (!FiniteNonnegative(charge) || charge > 1f) ||
            spec.CircuitIds == null || spec.CircuitIds.Count is < 1 or > 64 ||
            spec.CircuitIds.Any(id => !KsProcgenAreaNormalizer.ValidId(id)) ||
            spec.CircuitIds.Distinct(StringComparer.Ordinal).Count() != spec.CircuitIds.Count ||
            spec.CircuitPolicy == KsProcgenElectricalCircuitPolicy.Shared &&
            (spec.CircuitIds.Count != 1 || spec.CircuitIds[0] != "Main"))
        {
            issue = new("InvalidElectricalProfile", "Electrical policies, circuits or numeric bounds are invalid.");
            return false;
        }
        bool Entity(string id) => KsProcgenAreaNormalizer.ValidId(id) && (entityExists == null || entityExists(id));
        bool Choices(List<KsProcgenElectricalDeviceSpec> choices) => choices != null && choices.Count is >= 1 and <= 64 &&
            choices.All(choice => choice != null && Entity(choice.Prototype) && FiniteNonnegative(choice.Weight)) &&
            choices.Select(choice => choice.Prototype).Distinct(StringComparer.Ordinal).Count() == choices.Count &&
            choices.Any(choice => choice.Weight > 0f);
        if (!Entity(spec.HighVoltageCable) || !Entity(spec.MediumVoltageCable) || !Entity(spec.LowVoltageCable) ||
            !Choices(spec.Substations) || !Choices(spec.Apcs))
        {
            issue = new("InvalidElectricalPalette", "Cable and converter palettes need valid prototypes and positive-weight choices.");
            return false;
        }
        if (spec.Interfaces == null || spec.Interfaces.Count > 1024 || spec.Interfaces.Any(port => port == null ||
                !KsProcgenAreaNormalizer.ValidId(port.Id) || !KsProcgenAreaNormalizer.ValidId(port.NetworkKey) ||
                !spec.CircuitIds.Contains(port.CircuitId, StringComparer.Ordinal) || !Enum.IsDefined(port.Voltage) ||
                !Enum.IsDefined(port.Connection) || !FiniteNonnegative(port.AvailableWatts) ||
                Math.Abs((long) port.Cell.X) > 1_000_000 || Math.Abs((long) port.Cell.Y) > 1_000_000) ||
            spec.Interfaces.Select(port => port.Id).Distinct(StringComparer.Ordinal).Count() != spec.Interfaces.Count)
        {
            issue = new("InvalidElectricalInterface", "Interfaces need unique IDs, declared circuits, bounded cells and finite source declarations.");
            return false;
        }
        profile = new(spec, sourceProfileId);
        return true;
    }

    public static bool TryResolve(IPrototypeManager prototypeManager, string profileId,
        out KsProcgenElectricalProfile? profile, out KsProcgenIssue? issue, KsProcgenUtilityDraft? draft = null)
    {
        profile = null;
        if (draft != null && draft.TryElectrical(profileId, out profile))
        {
            issue = null;
            return true;
        }
        if (!KsProcgenAreaNormalizer.ValidId(profileId) ||
            !prototypeManager.TryIndex<KsProcgenElectricalProfilePrototype>(profileId, out var prototype))
        {
            issue = new("UnknownElectricalProfile", "The requested electrical profile does not exist.");
            return false;
        }
        return TrySnapshot(prototype, out profile, out issue, sourceProfileId: profileId,
            entityExists: id => prototypeManager.HasIndex<EntityPrototype>(id));
    }

    public static bool TryResolveSelection(IPrototypeManager prototypeManager, string? profileId,
        KsProcgenElectricalSpec? inlineOverride, KsProcgenElectricalProfile? inherited,
        out KsProcgenElectricalProfile? profile, out KsProcgenIssue? issue, KsProcgenUtilityDraft? draft = null)
    {
        profile = null;
        issue = null;
        if (profileId != null && !KsProcgenAreaNormalizer.ValidId(profileId))
        {
            issue = new("InvalidElectricalProfileReference", "Electrical profile references need bounded identifiers.");
            return false;
        }
        if (inlineOverride != null)
            return TrySnapshot(inlineOverride, out profile, out issue, entityExists: id => prototypeManager.HasIndex<EntityPrototype>(id));
        if (profileId != null) return TryResolve(prototypeManager, profileId, out profile, out issue, draft: draft);
        profile = inherited;
        return true;
    }

    internal static bool ValidateRequest(KsProcgenRequest request, out KsProcgenIssue? issue)
    {
        issue = null;
        if (request.ElectricalProfile != null && !KsProcgenAreaNormalizer.ValidId(request.ElectricalProfile))
        {
            issue = new("InvalidElectricalProfileReference", "Electrical profile references need bounded identifiers.");
            return false;
        }
        return request.Electrical == null || TrySnapshot(request.Electrical, out _, out issue);
    }

    internal static bool CheckPlanningSupport(IPrototypeManager prototypeManager, KsProcgenRequest request,
        out KsProcgenIssue? issue, string? themeId = null)
    {
        if (!TryResolveSelection(prototypeManager, request.ElectricalProfile, request.Electrical, null, out var profile, out issue))
            return false;
        if (profile == null && !string.IsNullOrWhiteSpace(themeId))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (seen.Add(themeId) && prototypeManager.TryIndex<KsProcgenRoomThemePrototype>(themeId, out var theme))
            {
                if (theme.ElectricalProfile != null)
                {
                    if (!TryResolve(prototypeManager, theme.ElectricalProfile, out profile, out issue)) return false;
                    break;
                }
                if (string.IsNullOrWhiteSpace(theme.Parent)) break;
                themeId = theme.Parent;
            }
        }
        if (profile == null) return true;
        issue = new("ElectricalPlanningUnsupported", "Electrical contracts are authorable but not yet retained and realized by this content plan.");
        return false;
    }

    private static bool FiniteNonnegative(float value) => float.IsFinite(value) && value >= 0f;
}
