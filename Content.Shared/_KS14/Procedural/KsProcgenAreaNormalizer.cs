using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenAreaProfile(string Id, KsProcgenMode Mode, KsProcgenGeometryMode GeometryMode,
    KsProcgenConnectivityPolicy ConnectivityPolicy, string? Theme, int MaxCells, int MaxAbsoluteCoordinate,
    string? EntranceConnections = null);
public sealed record KsProcgenAreaMarker(Vector2i Cell, string Channel, string ProfileId, string? BlobId = null);

/// <summary>Exact painted component; it grants no envelope, preserved-content copy or publication rights.</summary>
public sealed record KsProcgenAreaBlob(string Id, string Channel, KsProcgenAreaProfile Profile, IReadOnlyList<Vector2i> Cells)
{
    public IReadOnlyList<KsProcgenAreaEntrance> Entrances { get; init; } = [];
    public KsProcgenEntranceConnectionContract? ConnectionContract { get; init; }
    /// <summary>Copies geometry and complete entrance declarations. Theme selection consumes Profile.Theme separately.</summary>
    public KsProcgenRequest CreateRequest(int seed)
    {
        return new()
        {
            RequestId = Id, Seed = seed, Mode = Profile.Mode, GeometryMode = Profile.GeometryMode,
            ConnectivityPolicy = Profile.ConnectivityPolicy,
            Shape = new() { Cells = Cells.ToList() },
            Limits = new() { MaxCells = Profile.MaxCells, MaxAbsoluteCoordinate = Profile.MaxAbsoluteCoordinate },
            EntranceDomain = Entrances.Count == 0 && ConnectionContract == null ? null : KsProcgenEntranceRequestAdapter.Copy(this),
        };
    }
}

public sealed class KsProcgenAreaNormalization
{
    public KsProcgenStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenAreaBlob> Blobs { get; init; } = [];
    public ulong SnapshotHash { get; init; }
    public int MarkerCount { get; init; }
    public IReadOnlyList<KsProcgenAreaEntrance> Entrances { get; init; } = [];
}

/// <summary>Bounded, atomic cardinal paint normalization in one stable host frame.</summary>
public static class KsProcgenAreaNormalizer
{
    public static KsProcgenAreaNormalization Normalize(string hostKey, IReadOnlyList<KsProcgenAreaMarker> markers,
        IReadOnlyList<KsProcgenAreaProfile> profiles, IReadOnlySet<Vector2i>? keepVoid = null,
        int maximumMarkers = 65_536, int maximumBlobs = 4096,
        IReadOnlyList<KsProcgenEntranceMarker>? entrances = null, int maximumEntrances = 1024,
        IReadOnlyDictionary<string, IReadOnlyList<KsProcgenEntranceConfigurationSpec>>? connectionProfiles = null)
    {
        KsProcgenAreaNormalization Fail(KsProcgenStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "Area marker snapshot cannot be normalized atomically."),
            MarkerCount = markers.Count,
        };
        if (!ValidId(hostKey) || maximumMarkers is < 0 or > 65_536 || maximumBlobs is < 0 or > 4096 ||
            profiles.Count > 4096 || profiles.Any(profile => !ValidId(profile.Id) ||
                profile.Theme != null && !ValidId(profile.Theme) || !Enum.IsDefined(profile.Mode) ||
                profile.EntranceConnections != null && !ValidId(profile.EntranceConnections) ||
                !Enum.IsDefined(profile.GeometryMode) || !Enum.IsDefined(profile.ConnectivityPolicy) ||
                profile.MaxCells is < 1 or > 65_536 || profile.MaxAbsoluteCoordinate is < 1 or > 1_000_000) ||
            profiles.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count() != profiles.Count)
            return Fail(KsProcgenStatus.InvalidInput, "InvalidAreaMarkerInput");
        connectionProfiles ??= new Dictionary<string, IReadOnlyList<KsProcgenEntranceConfigurationSpec>>(StringComparer.Ordinal);
        if (connectionProfiles.Count > 4096 || connectionProfiles.Keys.Any(id => !ValidId(id)) ||
            profiles.Any(profile => profile.EntranceConnections != null && !connectionProfiles.ContainsKey(profile.EntranceConnections)))
            return Fail(KsProcgenStatus.InvalidInput, "InvalidEntranceConnectionProfile");
        keepVoid ??= new HashSet<Vector2i>();
        if (markers.Count > maximumMarkers || keepVoid.Count > 65_536)
            return Fail(KsProcgenStatus.BudgetExceeded, "AreaMarkerBudget");
        if (keepVoid.Any(cell => !ValidCell(cell, 1_000_000)))
            return Fail(KsProcgenStatus.InvalidInput, "InvalidAreaVoidCell");
        var definitions = profiles.ToDictionary(profile => profile.Id, StringComparer.Ordinal);
        var channels = new Dictionary<string, Dictionary<Vector2i, KsProcgenAreaMarker>>(StringComparer.Ordinal);
        foreach (var marker in markers)
        {
            if (!ValidId(marker.Channel) || !ValidId(marker.ProfileId) || marker.BlobId != null && !ValidId(marker.BlobId) ||
                !definitions.TryGetValue(marker.ProfileId, out var profile) || !ValidCell(marker.Cell, profile.MaxAbsoluteCoordinate))
                return Fail(KsProcgenStatus.InvalidInput, "InvalidAreaMarker");
            if (keepVoid.Contains(marker.Cell))
                continue;
            if (!channels.TryGetValue(marker.Channel, out var paint))
                channels.Add(marker.Channel, paint = new());
            if (paint.TryGetValue(marker.Cell, out var previous) && previous != marker)
                return Fail(KsProcgenStatus.InvalidInput, "ConflictingAreaPaint");
            paint[marker.Cell] = marker;
        }
        var claimed = new HashSet<Vector2i>();
        var blobIds = new HashSet<string>(StringComparer.Ordinal);
        var blobs = new List<KsProcgenAreaBlob>();
        foreach (var (channel, paint) in channels.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var cell in paint.Keys)
                if (!claimed.Add(cell))
                    return Fail(KsProcgenStatus.InvalidInput, "OverlappingAreaChannels");
            foreach (var cells in KsProcgenGeometry.ConnectedComponents(paint.Keys.ToHashSet()))
            {
                if (blobs.Count >= maximumBlobs)
                    return Fail(KsProcgenStatus.BudgetExceeded, "AreaBlobBudget");
                var profileIds = cells.Select(cell => paint[cell].ProfileId).Distinct(StringComparer.Ordinal).ToArray();
                var explicitIds = cells.Select(cell => paint[cell].BlobId).Where(id => id != null).Distinct(StringComparer.Ordinal).ToArray();
                if (profileIds.Length != 1 || explicitIds.Length > 1)
                    return Fail(KsProcgenStatus.InvalidInput, "ConflictingAreaComponent");
                var profile = definitions[profileIds[0]];
                if (cells.Count > profile.MaxCells)
                    return Fail(KsProcgenStatus.BudgetExceeded, "AreaProfileCellBudget");
                var ordered = KsProcgenGeometry.SortCells(cells);
                var identity = KsProcgenStableHash.Create();
                identity.AddString("ks-procgen-area-id-v1");
                identity.AddString(hostKey);
                identity.AddString(channel);
                AddCells(ref identity, ordered);
                var blobId = explicitIds.Length == 1 ? explicitIds[0]! : $"KsBlob{identity.Value:X16}";
                if (!blobIds.Add(blobId))
                    return Fail(KsProcgenStatus.InvalidInput, "DuplicateAreaBlobId");
                blobs.Add(new(blobId, channel, profile, ordered));
            }
        }
        var canonical = blobs.OrderBy(blob => blob.Id, StringComparer.Ordinal).ToList();
        var entrancePlan = KsProcgenEntranceNormalizer.Normalize(canonical, entrances ?? [], maximumEntrances: maximumEntrances);
        if (entrancePlan.Status is not (KsProcgenStatus.Success or KsProcgenStatus.NoOp))
            return new() { Status = entrancePlan.Status, Issue = entrancePlan.Issue, MarkerCount = markers.Count };
        canonical = canonical.Select(blob => blob with
        {
            Entrances = entrancePlan.Entrances.Where(entrance => entrance.BlobId == blob.Id).ToList().AsReadOnly(),
        }).ToList();
        for (var index = 0; index < canonical.Count; index++)
        {
            var blob = canonical[index];
            if (blob.Profile.EntranceConnections == null)
                continue;
            var contract = KsProcgenEntranceConnectionNormalizer.Normalize(blob.Id, blob.Entrances,
                connectionProfiles[blob.Profile.EntranceConnections], blob.Profile.ConnectivityPolicy);
            if (contract.Status != KsProcgenStatus.Success)
                return new() { Status = contract.Status, Issue = contract.Issue, MarkerCount = markers.Count };
            var domainCells = blob.Cells.ToHashSet();
            if (contract.Configurations.SelectMany(configuration => configuration.Groups).SelectMany(group => group.RootCells)
                .Any(cell => !domainCells.Contains(cell)))
                return Fail(KsProcgenStatus.InvalidInput, "EntranceRootOutsideDomain");
            canonical[index] = blob with { ConnectionContract = contract };
        }
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-area-snapshot-v2");
        hash.AddString(hostKey);
        AddCells(ref hash, KsProcgenGeometry.SortCells(keepVoid));
        hash.AddInt(canonical.Count);
        foreach (var blob in canonical)
        {
            hash.AddString(blob.Id);
            hash.AddString(blob.Channel);
            hash.AddString(blob.Profile.Id);
            hash.AddInt((int) blob.Profile.Mode);
            hash.AddInt((int) blob.Profile.GeometryMode);
            hash.AddInt((int) blob.Profile.ConnectivityPolicy);
            hash.AddString(blob.Profile.Theme ?? string.Empty);
            hash.AddString(blob.Profile.EntranceConnections ?? string.Empty);
            hash.AddInt(blob.Profile.MaxCells);
            hash.AddInt(blob.Profile.MaxAbsoluteCoordinate);
            AddCells(ref hash, blob.Cells);
            hash.AddString(blob.ConnectionContract?.ContractHash.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }
        hash.AddInt(entrancePlan.Entrances.Count);
        foreach (var entrance in entrancePlan.Entrances)
        {
            hash.AddString(entrance.BlobId);
            hash.AddString(entrance.PortId);
            hash.AddString(entrance.Channel);
            AddCells(ref hash, entrance.ThresholdCells);
            AddCells(ref hash, entrance.InsideApproach);
            AddCells(ref hash, entrance.OutsideApproach);
            hash.AddInt(entrance.InwardNormal.X);
            hash.AddInt(entrance.InwardNormal.Y);
            hash.AddInt(entrance.OptionalSealable ? 1 : 0);
        }
        return new()
        {
            Status = canonical.Count == 0 ? KsProcgenStatus.NoOp : KsProcgenStatus.Success,
            Blobs = canonical.AsReadOnly(), SnapshotHash = hash.Value, MarkerCount = markers.Count,
            Entrances = entrancePlan.Entrances,
        };
    }

    internal static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => character is >= '!' and <= '~');

    private static bool ValidCell(Vector2i cell, int maximum) => Math.Abs((long) cell.X) <= maximum && Math.Abs((long) cell.Y) <= maximum;

    private static void AddCells(ref KsProcgenStableHash hash, IReadOnlyList<Vector2i> cells)
    {
        hash.AddInt(cells.Count);
        foreach (var cell in cells)
        {
            hash.AddInt(cell.X);
            hash.AddInt(cell.Y);
        }
    }
}
