using System.Linq;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenAtmosphereMode : byte { Preserve, Fill, Vacuum }
public enum KsProcgenAtmosphereRequirement : byte { Required, Preferred }
public enum KsProcgenAtmosphereState : byte { InitialOnly, StableAtRest }

/// <summary>Portable named-gas authoring data. This never grants permission to mutate an atmosphere.</summary>
[DataDefinition]
[Virtual]
public partial class KsProcgenAtmosphereSpec
{
    [DataField] public int SchemaVersion = 1;
    [DataField] public KsProcgenAtmosphereMode Mode = KsProcgenAtmosphereMode.Preserve;
    [DataField] public float? TemperatureKelvin;
    [DataField] public float? PressureKPa;
    [DataField] public Dictionary<string, float> MoleFractions = new(StringComparer.Ordinal);
    [DataField] public KsProcgenAtmosphereRequirement Requirement = KsProcgenAtmosphereRequirement.Required;
    [DataField] public KsProcgenAtmosphereState StateRequirement = KsProcgenAtmosphereState.InitialOnly;
    [DataField] public int ObservationTicks;
    [DataField] public float PressureToleranceKPa = 0.1f;
    [DataField] public float TemperatureToleranceKelvin = 0.1f;
    [DataField] public float FractionTolerance = 0.001f;
}

[Prototype]
public sealed partial class KsProcgenAtmosphereProfilePrototype : KsProcgenAtmosphereSpec, IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
}

public sealed record KsProcgenGasFraction(string GasId, float Fraction);

/// <summary>Validated detached profile; only the normalizer can construct an instance.</summary>
public sealed class KsProcgenAtmosphereProfile
{
    internal KsProcgenAtmosphereProfile(KsProcgenAtmosphereSpec spec, string? sourceProfileId,
        IReadOnlyList<KsProcgenGasFraction> fractions)
    {
        SourceProfileId = sourceProfileId;
        Mode = spec.Mode;
        TemperatureKelvin = spec.TemperatureKelvin;
        PressureKPa = spec.PressureKPa;
        MoleFractions = KsProcgenReadOnly.Copy(fractions);
        Requirement = spec.Requirement;
        StateRequirement = spec.StateRequirement;
        ObservationTicks = spec.ObservationTicks;
        PressureToleranceKPa = PositiveZero(spec.PressureToleranceKPa);
        TemperatureToleranceKelvin = PositiveZero(spec.TemperatureToleranceKelvin);
        FractionTolerance = PositiveZero(spec.FractionTolerance);
        if (PressureKPa == 0f)
            PressureKPa = 0f;
    }

    public int SchemaVersion => 1;
    public string? SourceProfileId { get; }
    public KsProcgenAtmosphereMode Mode { get; }
    public float? TemperatureKelvin { get; }
    public float? PressureKPa { get; }
    public IReadOnlyList<KsProcgenGasFraction> MoleFractions { get; }
    public KsProcgenAtmosphereRequirement Requirement { get; }
    public KsProcgenAtmosphereState StateRequirement { get; }
    public int ObservationTicks { get; }
    public float PressureToleranceKPa { get; }
    public float TemperatureToleranceKelvin { get; }
    public float FractionTolerance { get; }

    /// <summary>Returns independent editable data, never the shared prototype's mutable fields.</summary>
    public KsProcgenAtmosphereSpec CopySpec() => new()
    {
        Mode = Mode, TemperatureKelvin = TemperatureKelvin, PressureKPa = PressureKPa,
        MoleFractions = MoleFractions.ToDictionary(fraction => fraction.GasId, fraction => fraction.Fraction, StringComparer.Ordinal),
        Requirement = Requirement, StateRequirement = StateRequirement, ObservationTicks = ObservationTicks,
        PressureToleranceKPa = PressureToleranceKPa, TemperatureToleranceKelvin = TemperatureToleranceKelvin,
        FractionTolerance = FractionTolerance,
    };

    public ulong SemanticHash
    {
        get
        {
            var hash = KsProcgenStableHash.Create();
            hash.AddString("ks-procgen-atmosphere-v1");
            AddHash(ref hash);
            return hash.Value;
        }
    }

    internal void AddHash(ref KsProcgenStableHash hash)
    {
        hash.AddInt(SchemaVersion);
        hash.AddString(SourceProfileId ?? string.Empty);
        hash.AddInt((int) Mode);
        hash.AddInt(TemperatureKelvin.HasValue ? 1 : 0);
        hash.AddInt(BitConverter.SingleToInt32Bits(TemperatureKelvin ?? 0f));
        hash.AddInt(PressureKPa.HasValue ? 1 : 0);
        hash.AddInt(BitConverter.SingleToInt32Bits(PressureKPa ?? 0f));
        hash.AddInt((int) Requirement);
        hash.AddInt((int) StateRequirement);
        hash.AddInt(ObservationTicks);
        hash.AddInt(BitConverter.SingleToInt32Bits(PressureToleranceKPa));
        hash.AddInt(BitConverter.SingleToInt32Bits(TemperatureToleranceKelvin));
        hash.AddInt(BitConverter.SingleToInt32Bits(FractionTolerance));
        hash.AddInt(MoleFractions.Count);
        foreach (var fraction in MoleFractions)
        {
            hash.AddString(fraction.GasId);
            hash.AddInt(BitConverter.SingleToInt32Bits(fraction.Fraction));
        }
    }

    private static float PositiveZero(float value) => value == 0f ? 0f : value;
}

/// <summary>Bounded profile validation/resolution independent of tile, gas or entity allocation.</summary>
public static class KsProcgenAtmosphereProfiles
{
    public const int MaximumObservationTicks = 3600;

    public static bool TrySnapshot(KsProcgenAtmosphereSpec spec, out KsProcgenAtmosphereProfile? profile,
        out KsProcgenIssue? issue, string? sourceProfileId = null, Func<string, bool>? gasExists = null)
    {
        profile = null;
        issue = null;
        if (spec == null || spec.SchemaVersion != 1)
        {
            issue = new("UnsupportedAtmosphereSchema", "Atmosphere schema version 1 is required.");
            return false;
        }
        if (sourceProfileId != null && !KsProcgenAreaNormalizer.ValidId(sourceProfileId) ||
            !Enum.IsDefined(spec.Mode) || !Enum.IsDefined(spec.Requirement) || !Enum.IsDefined(spec.StateRequirement) ||
            spec.MoleFractions == null || spec.MoleFractions.Count > Atmospherics.TotalNumberOfGases ||
            !FiniteNonnegative(spec.PressureToleranceKPa) || !FiniteNonnegative(spec.TemperatureToleranceKelvin) ||
            !FiniteNonnegative(spec.FractionTolerance) || spec.FractionTolerance > 1f ||
            spec.ObservationTicks is < 0 or > MaximumObservationTicks ||
            (spec.StateRequirement == KsProcgenAtmosphereState.InitialOnly) != (spec.ObservationTicks == 0))
        {
            issue = new("InvalidAtmosphereProfile", "Atmosphere policies, tolerances or observation bounds are invalid.");
            return false;
        }
        if (spec.Mode == KsProcgenAtmosphereMode.Preserve)
        {
            if (spec.TemperatureKelvin.HasValue || spec.PressureKPa.HasValue || spec.MoleFractions.Count != 0 ||
                spec.StateRequirement != KsProcgenAtmosphereState.InitialOnly)
            {
                issue = new("InvalidPreservedAtmosphere", "Preserve cannot declare a replacement gas state or observation.");
                return false;
            }
        }
        else if (spec.TemperatureKelvin is not { } temperature || !float.IsFinite(temperature) ||
                 temperature < Atmospherics.TCMB || temperature > Atmospherics.Tmax ||
                 spec.PressureKPa is not { } pressure || !FiniteNonnegative(pressure) ||
                 (spec.Mode == KsProcgenAtmosphereMode.Fill ? pressure == 0f || spec.MoleFractions.Count == 0
                     : pressure != 0f || spec.MoleFractions.Count != 0))
        {
            issue = new("InvalidAtmosphereState", "Fill needs positive pressure and gas; vacuum needs zero pressure and no gas. Both need an engine-supported temperature.");
            return false;
        }

        var sum = 0.0;
        foreach (var (gasId, fraction) in spec.MoleFractions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!TryGas(gasId, out _) || gasExists != null && !gasExists(gasId) || !FiniteNonnegative(fraction) || fraction > 1f)
            {
                issue = new("InvalidAtmosphereGas", "Each fraction needs a supported, exactly named gas and a finite value in [0,1].");
                return false;
            }
            sum += (double) fraction;
        }
        if (spec.Mode == KsProcgenAtmosphereMode.Fill && Math.Abs(sum - 1.0) > 0.000001)
        {
            issue = new("AtmosphereFractionsNotNormalized", "Mole fractions must sum to one within 0.000001.");
            return false;
        }

        // Rounding normalized values back to float can itself move the sum by up to 2^-24.
        // Keep that representation unchanged so reopening an editable snapshot cannot drift its hash.
        var normalizeFractions = Math.Abs(sum - 1.0) > 1.0 / 16_777_216.0;
        var fractions = spec.MoleFractions.Where(pair => pair.Value > 0f).OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new KsProcgenGasFraction(pair.Key,
                normalizeFractions ? (float) ((double) pair.Value / sum) : pair.Value)).ToArray();
        profile = new(spec, sourceProfileId, fractions);
        return true;
    }

    public static bool TryResolve(IPrototypeManager prototypeManager, string profileId,
        out KsProcgenAtmosphereProfile? profile, out KsProcgenIssue? issue, KsProcgenUtilityDraft? draft = null)
    {
        profile = null;
        if (draft != null && draft.TryAtmosphere(profileId, out profile))
        {
            issue = null;
            return true;
        }
        if (!KsProcgenAreaNormalizer.ValidId(profileId) ||
            !prototypeManager.TryIndex<KsProcgenAtmosphereProfilePrototype>(profileId, out var prototype))
        {
            issue = new("UnknownAtmosphereProfile", "The requested atmosphere profile does not exist.");
            return false;
        }
        return TrySnapshot(prototype, out profile, out issue, sourceProfileId: profileId,
            gasExists: gasId => prototypeManager.HasIndex<GasPrototype>(gasId));
    }

    /// <summary>Inline instance override, then explicit profile, then an already resolved theme default.</summary>
    public static bool TryResolveSelection(IPrototypeManager prototypeManager, string? profileId,
        KsProcgenAtmosphereSpec? inlineOverride, KsProcgenAtmosphereProfile? inherited,
        out KsProcgenAtmosphereProfile? profile, out KsProcgenIssue? issue, KsProcgenUtilityDraft? draft = null)
    {
        profile = null;
        issue = null;
        if (profileId != null && !KsProcgenAreaNormalizer.ValidId(profileId))
        {
            issue = new("InvalidAtmosphereProfileReference", "Atmosphere profile IDs must be bounded nonempty identifiers.");
            return false;
        }
        if (inlineOverride != null)
            return TrySnapshot(inlineOverride, out profile, out issue,
                gasExists: gasId => prototypeManager.HasIndex<GasPrototype>(gasId));
        if (profileId != null)
            return TryResolve(prototypeManager, profileId, out profile, out issue, draft: draft);
        profile = inherited;
        return true;
    }

    internal static bool ValidateRequest(KsProcgenRequest request, out KsProcgenIssue? issue)
    {
        issue = null;
        if (request.AtmosphereProfile != null && !KsProcgenAreaNormalizer.ValidId(request.AtmosphereProfile))
        {
            issue = new("InvalidAtmosphereProfileReference", "Atmosphere profile IDs must be bounded nonempty identifiers.");
            return false;
        }
        return request.Atmosphere == null || TrySnapshot(request.Atmosphere, out _, out issue);
    }

    /// <summary>Keep existing content planners from silently discarding newly accepted authoring data.</summary>
    internal static bool CheckPlanningSupport(IPrototypeManager prototypeManager, KsProcgenRequest request,
        out KsProcgenIssue? issue, string? themeId = null)
    {
        if (!TryResolveSelection(prototypeManager, request.AtmosphereProfile, request.Atmosphere, null, out var profile, out issue))
            return false;
        if (profile == null && themeId != null)
        {
            // Check before packing as a prefab-only plan may never run per-room theme selection.
            // Leave unrelated missing/cyclic theme validation to the existing theme validator.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (seen.Add(themeId) && prototypeManager.TryIndex<KsProcgenRoomThemePrototype>(themeId, out var theme))
            {
                if (theme.AtmosphereProfile != null)
                {
                    if (!TryResolve(prototypeManager, theme.AtmosphereProfile, out profile, out issue))
                        return false;
                    break;
                }
                if (string.IsNullOrWhiteSpace(theme.Parent))
                    break;
                themeId = theme.Parent;
            }
        }
        if (profile == null) return true;
        issue = new("AtmospherePlanningUnsupported", "Atmosphere contracts are authorable, but not yet integrated into this content plan.");
        return false;
    }

    internal static bool TryGas(string gasId, out Gas gas) => Enum.TryParse(gasId, out gas) && Enum.IsDefined(gas) &&
        (int) gas >= 0 && (int) gas < Atmospherics.TotalNumberOfGases && gas.ToString() == gasId;

    private static bool FiniteNonnegative(float value) => float.IsFinite(value) && value >= 0f;
}
