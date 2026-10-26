using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Canonical, detached versioned request, including pinned utility declarations. This is input
/// identity, not a content manifest, writable-context certificate, generation plan or replay receipt.
/// Referenced room/theme/entity prototype bodies must still be pinned by the content snapshot.
/// </summary>
public sealed class KsProcgenRequestSnapshot
{
    private readonly KsProcgenRequest _request;
    public int SchemaVersion => _request.SchemaVersion;
    public string RequestId => _request.RequestId;
    public int Seed => _request.Seed;
    public ulong DeclarationHash { get; }
    public KsProcgenAtmosphereProfile? Atmosphere { get; }
    public KsProcgenElectricalProfile? Electrical { get; }
    public KsProcgenUtilityContract? Utilities { get; }

    private KsProcgenRequestSnapshot(KsProcgenRequest request, KsProcgenNormalizedShape shape,
        KsProcgenAtmosphereProfile? atmosphere, KsProcgenElectricalProfile? electrical, KsProcgenUtilityContract? utilities)
    {
        _request = request;
        Atmosphere = atmosphere;
        Electrical = electrical;
        Utilities = utilities;
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-request-v1");
        hash.AddInt(request.SchemaVersion); hash.AddString(request.RequestId); hash.AddInt(request.Seed);
        hash.AddInt((int) request.Mode); hash.AddInt((int) request.GeometryMode); hash.AddInt((int) request.ConnectivityPolicy);
        hash.AddInt(request.Limits.MaxCells); hash.AddInt(request.Limits.MaxAbsoluteCoordinate);
        AddCells(ref hash, request.Shape.Cells); AddCells(ref hash, request.Shape.EnvelopeCells);
        AddCells(ref hash, request.Shape.PreservedCells); AddCells(ref hash, request.Shape.VoidCells);
        AddCells(ref hash, request.RootCells);
        AddHash(ref hash, shape.ConstantContractHash);
        hash.AddInt(request.EntranceDomain == null ? 0 : 1);
        if (request.EntranceDomain is { } domain)
        {
            hash.AddString(domain.Channel);
            hash.AddInt(domain.Entrances.Count);
            foreach (var entrance in domain.Entrances)
            {
                hash.AddString(entrance.PortId); AddCells(ref hash, entrance.ThresholdCells);
                hash.AddInt(entrance.InwardNormal.X); hash.AddInt(entrance.InwardNormal.Y);
                hash.AddInt(entrance.OptionalSealable ? 1 : 0);
            }
            AddHash(ref hash, shape.EntranceContract!.ContractHash);
        }
        hash.AddInt(request.SizeMix.Count);
        foreach (var goal in request.SizeMix)
        {
            hash.AddString(goal.Id); hash.AddInt(goal.MinCells); hash.AddInt(goal.MaxCells); hash.AddInt(goal.TargetCount);
        }
        var windows = request.WindowGoal;
        hash.AddInt(BitConverter.SingleToInt32Bits(windows.ExteriorWindowFraction));
        hash.AddInt(windows.HardFraction ? 1 : 0); hash.AddInt(windows.ToleranceCells); hash.AddInt(windows.MinimumCount);
        hash.AddInt(windows.MaximumCount.HasValue ? 1 : 0); hash.AddInt(windows.MaximumCount ?? 0);
        var fallback = request.FallbackPolicy;
        hash.AddInt(fallback.AllowMergedPartition ? 1 : 0); hash.AddInt(fallback.AllowRoomSizeShortfall ? 1 : 0);
        hash.AddInt(fallback.AllowSparseFurnishing ? 1 : 0); hash.AddInt(fallback.AllowSparseLighting ? 1 : 0);
        hash.AddInt(atmosphere == null ? 0 : 1);
        if (atmosphere != null) atmosphere.AddHash(ref hash);
        hash.AddInt(electrical == null ? 0 : 1);
        if (electrical != null) electrical.AddHash(ref hash);
        // Preserve the identity of legacy schema-one requests without scoped contracts.
        if (utilities != null) utilities.AddHash(ref hash);
        DeclarationHash = hash.Value;
    }

    /// <summary>Fresh canonical editable data, suitable for engine serialization and recapture.</summary>
    public KsProcgenRequest CopyRequest() => Copy(_request, Atmosphere, Electrical, Utilities);

    /// <summary>
    /// Capture inline/legacy input without consulting loaded content. A named utility must have
    /// a complete inline override; otherwise use the prototype-aware overload to resolve it first.
    /// </summary>
    public static bool TryCapture(KsProcgenRequest request, out KsProcgenRequestSnapshot? snapshot, out KsProcgenIssue? issue)
    {
        snapshot = null;
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out issue)) return false;
        if (request.AtmosphereProfile != null && request.Atmosphere == null || request.ElectricalProfile != null && request.Electrical == null)
        {
            issue = new("UnresolvedUtilityReference", "Named utilities must be resolved before capturing a request.");
            return false;
        }
        KsProcgenAtmosphereProfile? atmosphere = null;
        KsProcgenElectricalProfile? electrical = null;
        if (request.Atmosphere != null && !KsProcgenAtmosphereProfiles.TrySnapshot(request.Atmosphere, out atmosphere,
                out issue, sourceProfileId: request.AtmosphereProfile)) return false;
        if (request.Electrical != null && !KsProcgenElectricalProfiles.TrySnapshot(request.Electrical, out electrical,
                out issue, sourceProfileId: request.ElectricalProfile)) return false;

        KsProcgenUtilityContract? utilities = null;
        if (request.Utilities != null && !KsProcgenUtilityContracts.TryCapture(request.Utilities, request.RequestId, shape!,
                request.Limits.MaxCells, out utilities, out issue)) return false;
        if (!ValidateUtilities(atmosphere, electrical, utilities, out issue)) return false;
        var canonical = Copy(request, atmosphere, electrical, utilities);
        canonical.Shape = new()
        {
            Cells = shape!.TargetCells.ToList(), EnvelopeCells = shape.EnvelopeCells.ToList(),
            // Constant cells are reintroduced by their own declarations when this request is read.
            PreservedCells = shape.PreservedCells.Where(cell => !shape.TryGetConstantOwner(cell, out _)).ToList(),
            VoidCells = shape.VoidCells.ToList(),
        };
        canonical.RootCells = KsProcgenGeometry.SortCells(request.RootCells.Distinct()).ToList();
        canonical.ConstantRegions = canonical.ConstantRegions.OrderBy(region => region.Id, StringComparer.Ordinal).ToList();
        foreach (var region in canonical.ConstantRegions)
        {
            region.LocalCells = KsProcgenGeometry.SortCells(region.LocalCells).ToList();
            region.Ports = region.Ports.OrderBy(port => port.Id, StringComparer.Ordinal).ToList();
        }
        canonical.SizeMix = canonical.SizeMix.OrderBy(goal => goal.Id, StringComparer.Ordinal).ToList();
        canonical.WindowGoal.ExteriorWindowFraction = canonical.WindowGoal.ExteriorWindowFraction == 0f ? 0f : canonical.WindowGoal.ExteriorWindowFraction;
        canonical.Atmosphere = atmosphere?.CopySpec();
        canonical.Electrical = electrical?.CopySpec();
        if (request.EntranceDomain is { } domain)
        {
            canonical.EntranceDomain = new()
            {
                Channel = domain.Channel,
                Entrances = shape.Entrances.OrderBy(entrance => entrance.PortId, StringComparer.Ordinal).Select(entrance => new KsProcgenEntranceSpec
                {
                    PortId = entrance.PortId, ThresholdCells = entrance.ThresholdCells.ToList(),
                    InwardNormal = entrance.InwardNormal, OptionalSealable = entrance.OptionalSealable,
                }).ToList(),
                Configurations = shape.EntranceContract!.Configurations.Select(configuration => new KsProcgenEntranceConfigurationSpec
                {
                    Id = configuration.Id, Weight = configuration.Weight, Mode = configuration.Mode, IsolationScope = configuration.IsolationScope,
                    Groups = configuration.Groups.Select(group => new KsProcgenEntranceGroupSpec
                    {
                        Id = group.Id, Ports = group.Ports.ToList(), RootCells = group.RootCells.ToList(),
                    }).ToList(), SealedPorts = configuration.SealedPorts.ToList(),
                }).ToList(),
            };
        }
        snapshot = new(canonical, shape, atmosphere, electrical, utilities);
        return true;
    }

    /// <summary>Resolve named utilities from a validated draft or loaded prototypes before detaching.</summary>
    public static bool TryCapture(IPrototypeManager manager, KsProcgenRequest request,
        out KsProcgenRequestSnapshot? snapshot, out KsProcgenIssue? issue, KsProcgenUtilityDraft? draft = null)
    {
        snapshot = null;
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out issue)) return false;
        if (!KsProcgenAtmosphereProfiles.TryResolveSelection(manager, request.AtmosphereProfile, request.Atmosphere, null,
                out var atmosphere, out issue, draft: draft) ||
            !KsProcgenElectricalProfiles.TryResolveSelection(manager, request.ElectricalProfile, request.Electrical, null,
                out var electrical, out issue, draft: draft)) return false;
        KsProcgenUtilityContract? utilities = null;
        if (request.Utilities != null && !KsProcgenUtilityContracts.TryCapture(request.Utilities, request.RequestId, shape!,
                request.Limits.MaxCells, out utilities, out issue, manager: manager, draft: draft)) return false;
        var detached = Copy(request, atmosphere, electrical, utilities);
        return TryCapture(detached, out snapshot, out issue);
    }

    private static KsProcgenRequest Copy(KsProcgenRequest request, KsProcgenAtmosphereProfile? atmosphere,
        KsProcgenElectricalProfile? electrical, KsProcgenUtilityContract? utilities) => new()
    {
        SchemaVersion = request.SchemaVersion, RequestId = request.RequestId, Seed = request.Seed, Mode = request.Mode,
        GeometryMode = request.GeometryMode, ConnectivityPolicy = request.ConnectivityPolicy,
        Limits = new() { MaxCells = request.Limits.MaxCells, MaxAbsoluteCoordinate = request.Limits.MaxAbsoluteCoordinate },
        Shape = new()
        {
            Cells = request.Shape.Cells.ToList(), EnvelopeCells = request.Shape.EnvelopeCells.ToList(),
            PreservedCells = request.Shape.PreservedCells.ToList(), VoidCells = request.Shape.VoidCells.ToList(),
            TextMask = request.Shape.TextMask, TextOrigin = request.Shape.TextOrigin,
            AddRectangles = request.Shape.AddRectangles.Select(rect => new KsProcgenTileRect { Min = rect.Min, Max = rect.Max }).ToList(),
            SubtractRectangles = request.Shape.SubtractRectangles.Select(rect => new KsProcgenTileRect { Min = rect.Min, Max = rect.Max }).ToList(),
        },
        RootCells = request.RootCells.ToList(),
        ConstantRegions = request.ConstantRegions.Select(region => new KsProcgenConstantRegionSpec
        {
            Id = region.Id, SourceId = region.SourceId, ContentFingerprint = region.ContentFingerprint, Origin = region.Origin,
            QuarterTurns = region.QuarterTurns, LocalCells = region.LocalCells.ToList(),
            Ports = region.Ports.Select(port => new KsProcgenConstantPortSpec
                { Id = port.Id, Threshold = port.Threshold, OutwardNormal = port.OutwardNormal }).ToList(),
        }).ToList(),
        EntranceDomain = request.EntranceDomain is not { } domain ? null : new()
        {
            Channel = domain.Channel,
            Entrances = domain.Entrances.Select(entrance => new KsProcgenEntranceSpec
            {
                PortId = entrance.PortId, ThresholdCells = entrance.ThresholdCells.ToList(),
                InwardNormal = entrance.InwardNormal, OptionalSealable = entrance.OptionalSealable,
            }).ToList(),
            Configurations = domain.Configurations.Select(configuration => new KsProcgenEntranceConfigurationSpec
            {
                Id = configuration.Id, Weight = configuration.Weight, Mode = configuration.Mode, IsolationScope = configuration.IsolationScope,
                Groups = configuration.Groups.Select(group => new KsProcgenEntranceGroupSpec
                    { Id = group.Id, Ports = group.Ports.ToList(), RootCells = group.RootCells.ToList() }).ToList(),
                SealedPorts = configuration.SealedPorts.ToList(),
            }).ToList(),
        },
        SizeMix = request.SizeMix.Select(goal => new KsProcgenRoomSizeGoal
            { Id = goal.Id, MinCells = goal.MinCells, MaxCells = goal.MaxCells, TargetCount = goal.TargetCount }).ToList(),
        WindowGoal = new()
        {
            ExteriorWindowFraction = request.WindowGoal.ExteriorWindowFraction, HardFraction = request.WindowGoal.HardFraction,
            ToleranceCells = request.WindowGoal.ToleranceCells, MinimumCount = request.WindowGoal.MinimumCount, MaximumCount = request.WindowGoal.MaximumCount,
        },
        FallbackPolicy = new()
        {
            AllowMergedPartition = request.FallbackPolicy.AllowMergedPartition, AllowRoomSizeShortfall = request.FallbackPolicy.AllowRoomSizeShortfall,
            AllowSparseFurnishing = request.FallbackPolicy.AllowSparseFurnishing, AllowSparseLighting = request.FallbackPolicy.AllowSparseLighting,
        },
        AtmosphereProfile = request.AtmosphereProfile, ElectricalProfile = request.ElectricalProfile,
        Atmosphere = atmosphere?.CopySpec(), Electrical = electrical?.CopySpec(),
        Utilities = utilities?.CopySpec(),
    };

    private static bool ValidateUtilities(KsProcgenAtmosphereProfile? atmosphere, KsProcgenElectricalProfile? electrical,
        KsProcgenUtilityContract? utilities, out KsProcgenIssue? issue)
    {
        issue = null;
        if (utilities == null) return true;
        if (atmosphere != null && utilities.GasAssignments.Any(gas => gas.Scope == KsProcgenUtilityScope.Request &&
                !KsProcgenUtilityContracts.SameGasContract(gas.Profile, atmosphere)))
        { issue = new("ConflictingRequestGasAssignments", "Top-level and scoped request gas assignments must agree."); return false; }
        // Without an explicit profile, circuits must be bound against the eventual room/theme profile.
        if (electrical != null && utilities.Consumers.Any(consumer => !electrical.CircuitIds.Contains(consumer.CircuitId)))
        { issue = new("UnknownConsumerCircuit", "A consumer names a circuit absent from the explicit electrical profile."); return false; }
        return true;
    }

    private static void AddCells(ref KsProcgenStableHash hash, IReadOnlyList<Vector2i> cells)
    {
        hash.AddInt(cells.Count);
        foreach (var cell in cells) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
    }

    private static void AddHash(ref KsProcgenStableHash hash, ulong value)
    {
        hash.AddInt(unchecked((int) value));
        hash.AddInt(unchecked((int) (value >> 32)));
    }
}
