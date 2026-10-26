using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenElectricalFloorDomain : byte { UsableRoomFloor, AllRoomFloor }
public enum KsProcgenElectricalSquarePolicy : byte { ForbidNew, RequireNecessityCertificate }

/// <summary>
/// Schema-two layout intent. These declarations do not establish a closet, service coverage or
/// a necessity proof. Native topology and operating supply remain separate acceptance conditions.
/// </summary>
[DataDefinition]
public sealed partial class KsProcgenElectricalDistributionSpec
{
    [DataField] public List<Vector2i> ClosetInteriorSizes = new() { new(1, 2), new(2, 2) };
    [DataField] public string ClosetTheme = "KsProcgenElectricalCloset";
    [DataField] public bool MediumVoltageCirculationBackbone = true;
    [DataField] public bool MediumVoltageRoomEntryBranches = true;
    /// <summary>Fraction of eligible room-floor cells that must receive geometric LV service.</summary>
    [DataField] public float FloorServiceFraction = 1f;
    [DataField] public KsProcgenElectricalFloorDomain FloorServiceDomain = KsProcgenElectricalFloorDomain.UsableRoomFloor;
    [DataField] public KsProcgenElectricalSquarePolicy LowVoltageSquares = KsProcgenElectricalSquarePolicy.RequireNecessityCertificate;
}

/// <summary>Detached distribution intent. Closet rotations are equivalent; the size list is a set.</summary>
public sealed class KsProcgenElectricalDistribution
{
    internal KsProcgenElectricalDistribution(KsProcgenElectricalDistributionSpec spec)
    {
        ClosetInteriorSizes = spec.ClosetInteriorSizes.Select(size => new Vector2i(Math.Min(size.X, size.Y), Math.Max(size.X, size.Y)))
            .OrderBy(size => size.X).ThenBy(size => size.Y).ToReadOnlyList();
        ClosetTheme = spec.ClosetTheme;
        MediumVoltageCirculationBackbone = spec.MediumVoltageCirculationBackbone;
        MediumVoltageRoomEntryBranches = spec.MediumVoltageRoomEntryBranches;
        FloorServiceFraction = spec.FloorServiceFraction == 0f ? 0f : spec.FloorServiceFraction;
        FloorServiceDomain = spec.FloorServiceDomain;
        LowVoltageSquares = spec.LowVoltageSquares;
    }

    public IReadOnlyList<Vector2i> ClosetInteriorSizes { get; }
    public string ClosetTheme { get; }
    public bool MediumVoltageCirculationBackbone { get; }
    public bool MediumVoltageRoomEntryBranches { get; }
    public float FloorServiceFraction { get; }
    public KsProcgenElectricalFloorDomain FloorServiceDomain { get; }
    public KsProcgenElectricalSquarePolicy LowVoltageSquares { get; }

    public KsProcgenElectricalDistributionSpec CopySpec() => new()
    {
        ClosetInteriorSizes = ClosetInteriorSizes.ToList(), ClosetTheme = ClosetTheme,
        MediumVoltageCirculationBackbone = MediumVoltageCirculationBackbone,
        MediumVoltageRoomEntryBranches = MediumVoltageRoomEntryBranches,
        FloorServiceFraction = FloorServiceFraction, FloorServiceDomain = FloorServiceDomain,
        LowVoltageSquares = LowVoltageSquares,
    };

    internal void AddHash(ref KsProcgenStableHash hash)
    {
        hash.AddInt(ClosetInteriorSizes.Count);
        foreach (var size in ClosetInteriorSizes) { hash.AddInt(size.X); hash.AddInt(size.Y); }
        hash.AddString(ClosetTheme);
        hash.AddInt(MediumVoltageCirculationBackbone ? 1 : 0); hash.AddInt(MediumVoltageRoomEntryBranches ? 1 : 0);
        hash.AddInt(BitConverter.SingleToInt32Bits(FloorServiceFraction));
        hash.AddInt((int) FloorServiceDomain); hash.AddInt((int) LowVoltageSquares);
    }

    internal static bool Validate(KsProcgenElectricalDistributionSpec spec, out KsProcgenIssue? issue)
    {
        issue = null;
        if (spec.ClosetInteriorSizes == null || spec.ClosetInteriorSizes.Count is < 1 or > 2 ||
            spec.ClosetInteriorSizes.Any(size => size != new Vector2i(1, 2) && size != new Vector2i(2, 1) && size != new Vector2i(2, 2)) ||
            spec.ClosetInteriorSizes.Select(size => size.X * size.Y).Distinct().Count() != spec.ClosetInteriorSizes.Count ||
            !KsProcgenAreaNormalizer.ValidId(spec.ClosetTheme) || !float.IsFinite(spec.FloorServiceFraction) ||
            spec.FloorServiceFraction is < 0f or > 1f || !Enum.IsDefined(spec.FloorServiceDomain) || !Enum.IsDefined(spec.LowVoltageSquares))
        {
            issue = new("InvalidElectricalDistribution", "Distribution needs unique 1x2/2x2 closets, a theme, bounded floor coverage and a strict LV square policy.");
            return false;
        }
        return true;
    }
}
