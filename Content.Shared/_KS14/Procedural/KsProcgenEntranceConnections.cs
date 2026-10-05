using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public enum KsProcgenEntranceConnectionMode : byte
{
    ExactComponents,
    RequiredConnections,
}

public enum KsProcgenEntranceIsolationScope : byte
{
    DomainLocal,
    RequestNetwork,
}

[DataDefinition]
public sealed partial class KsProcgenEntranceGroupSpec
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public List<string> Ports = [];
    [DataField] public List<Vector2i> RootCells = [];
}

[DataDefinition]
public sealed partial class KsProcgenEntranceConfigurationSpec
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField] public float Weight = 1f;
    [DataField] public KsProcgenEntranceConnectionMode Mode = KsProcgenEntranceConnectionMode.ExactComponents;
    [DataField] public KsProcgenEntranceIsolationScope IsolationScope = KsProcgenEntranceIsolationScope.DomainLocal;
    [DataField(required: true)] public List<KsProcgenEntranceGroupSpec> Groups = [];
    [DataField] public List<string> SealedPorts = [];
}

[Prototype("ksProcgenEntranceConnections")]
public sealed partial class KsProcgenEntranceConnectionsPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField(required: true)] public List<KsProcgenEntranceConfigurationSpec> Configurations = [];
}

public sealed record KsProcgenEntranceGroup(string Id, IReadOnlyList<string> Ports)
{
    public IReadOnlyList<Vector2i> RootCells { get; init; } = [];
}
public sealed record KsProcgenEntranceConfiguration(string Id, float Weight, KsProcgenEntranceConnectionMode Mode,
    KsProcgenEntranceIsolationScope IsolationScope, IReadOnlyList<KsProcgenEntranceGroup> Groups, IReadOnlyList<string> SealedPorts);

/// <summary>Validated alternatives, not a selected configuration or proof of any route.</summary>
public sealed class KsProcgenEntranceConnectionContract
{
    public KsProcgenStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public IReadOnlyList<KsProcgenEntranceConfiguration> Configurations { get; init; } = [];
    public ulong ContractHash { get; init; }
}

public static class KsProcgenEntranceConnectionNormalizer
{
    public static KsProcgenEntranceConnectionContract Normalize(string blobId, IReadOnlyList<KsProcgenAreaEntrance> entrances,
        IReadOnlyList<KsProcgenEntranceConfigurationSpec> configurations, KsProcgenConnectivityPolicy connectivityPolicy)
    {
        KsProcgenEntranceConnectionContract Fail(string code, string configurationId = "") => new()
        {
            Status = KsProcgenStatus.InvalidInput,
            Issue = new(code, $"Entrance contract for blob '{blobId}', configuration '{configurationId}' is invalid."),
        };
        if (!KsProcgenAreaNormalizer.ValidId(blobId) || !Enum.IsDefined(connectivityPolicy) || entrances.Count > 1024 ||
            configurations.Count is < 1 or > 64 || entrances.Any(entrance => entrance.BlobId != blobId ||
                !KsProcgenAreaNormalizer.ValidId(entrance.PortId)) ||
            entrances.Select(entrance => entrance.PortId).Distinct(StringComparer.Ordinal).Count() != entrances.Count)
            return Fail("InvalidEntranceConnectionInput");
        var ports = entrances.ToDictionary(entrance => entrance.PortId, StringComparer.Ordinal);
        var configurationIds = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<KsProcgenEntranceConfiguration>();
        foreach (var configuration in configurations)
        {
            if (configuration == null || !KsProcgenAreaNormalizer.ValidId(configuration.Id) || !configurationIds.Add(configuration.Id) ||
                !float.IsFinite(configuration.Weight) || configuration.Weight < 0f ||
                !Enum.IsDefined(configuration.Mode) || !Enum.IsDefined(configuration.IsolationScope) ||
                configuration.Groups == null || configuration.SealedPorts == null || configuration.Groups.Count > 1024 ||
                configuration.SealedPorts.Count > 1024 || configuration.Groups.Count + configuration.SealedPorts.Count == 0)
                return Fail("InvalidEntranceConfiguration", configurationId: configuration?.Id ?? string.Empty);
            var covered = new HashSet<string>(StringComparer.Ordinal);
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            var groups = new List<KsProcgenEntranceGroup>();
            var rootCount = 0;
            foreach (var group in configuration.Groups)
            {
                if (group == null || !KsProcgenAreaNormalizer.ValidId(group.Id) || !groupIds.Add(group.Id) ||
                    group.Ports == null || group.Ports.Count is < 1 or > 1024 || group.RootCells == null ||
                    group.RootCells.Count > 1024 || group.RootCells.Distinct().Count() != group.RootCells.Count ||
                    group.RootCells.Any(cell => Math.Abs((long) cell.X) > 1_000_000 || Math.Abs((long) cell.Y) > 1_000_000))
                    return Fail("InvalidEntranceGroup", configurationId: configuration.Id);
                foreach (var port in group.Ports)
                    if (!KsProcgenAreaNormalizer.ValidId(port) || !ports.ContainsKey(port) || !covered.Add(port))
                        return Fail("InvalidEntranceMembership", configurationId: configuration.Id);
                if (connectivityPolicy == KsProcgenConnectivityPolicy.DeclaredNetworks && group.RootCells.Count == 0)
                    return Fail("MissingDeclaredNetworkRoot", configurationId: configuration.Id);
                rootCount += group.RootCells.Count;
                if (rootCount > 1024)
                    return Fail("EntranceRootBudget", configurationId: configuration.Id);
                groups.Add(new(group.Id, group.Ports.OrderBy(port => port, StringComparer.Ordinal).ToList().AsReadOnly())
                {
                    RootCells = KsProcgenGeometry.SortCells(group.RootCells).ToList().AsReadOnly(),
                });
            }
            foreach (var port in configuration.SealedPorts)
                if (!KsProcgenAreaNormalizer.ValidId(port) || !ports.TryGetValue(port, out var entrance) || !entrance.OptionalSealable || !covered.Add(port))
                    return Fail("InvalidEntranceSealing", configurationId: configuration.Id);
            if (covered.Count != ports.Count)
                return Fail("MissingEntranceDisposition", configurationId: configuration.Id);
            if (connectivityPolicy == KsProcgenConnectivityPolicy.SingleNetwork &&
                configuration.Mode == KsProcgenEntranceConnectionMode.ExactComponents &&
                configuration.IsolationScope == KsProcgenEntranceIsolationScope.RequestNetwork && groups.Count > 1)
                return Fail("ContradictoryEntranceNetworks", configurationId: configuration.Id);
            normalized.Add(new(configuration.Id, configuration.Weight == 0f ? 0f : configuration.Weight, configuration.Mode,
                configuration.IsolationScope, groups.OrderBy(group => group.Id, StringComparer.Ordinal).ToList().AsReadOnly(),
                configuration.SealedPorts.OrderBy(port => port, StringComparer.Ordinal).ToList().AsReadOnly()));
        }
        if (!normalized.Any(configuration => configuration.Weight > 0f))
            return Fail("NoEligibleEntranceConfigurations");
        var ordered = normalized.OrderBy(configuration => configuration.Id, StringComparer.Ordinal).ToList().AsReadOnly();
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-entrance-contract-v2");
        hash.AddString(blobId);
        hash.AddInt((int) connectivityPolicy);
        hash.AddInt(ports.Count);
        foreach (var port in entrances.OrderBy(entrance => entrance.PortId, StringComparer.Ordinal))
        {
            hash.AddString(port.PortId);
            hash.AddInt(port.OptionalSealable ? 1 : 0);
        }
        hash.AddInt(ordered.Count);
        foreach (var configuration in ordered)
        {
            hash.AddString(configuration.Id);
            hash.AddInt(BitConverter.SingleToInt32Bits(configuration.Weight));
            hash.AddInt((int) configuration.Mode);
            hash.AddInt((int) configuration.IsolationScope);
            hash.AddInt(configuration.Groups.Count);
            foreach (var group in configuration.Groups)
            {
                hash.AddString(group.Id);
                hash.AddInt(group.Ports.Count);
                foreach (var port in group.Ports)
                    hash.AddString(port);
                hash.AddInt(group.RootCells.Count);
                foreach (var cell in group.RootCells)
                {
                    hash.AddInt(cell.X);
                    hash.AddInt(cell.Y);
                }
            }
            hash.AddInt(configuration.SealedPorts.Count);
            foreach (var port in configuration.SealedPorts)
                hash.AddString(port);
        }
        return new() { Status = KsProcgenStatus.Success, Configurations = ordered, ContractHash = hash.Value };
    }
}

/// <summary>Import-only adapter. Configuration IDs are supplied by the author/editor and must be persisted.</summary>
public static class KsProcgenEntranceShorthand
{
    public static bool TryImport(string text, IReadOnlyList<string> configurationIds,
        out IReadOnlyList<KsProcgenEntranceConfigurationSpec> configurations, out KsProcgenIssue? issue,
        KsProcgenEntranceConnectionMode mode = KsProcgenEntranceConnectionMode.ExactComponents,
        KsProcgenEntranceIsolationScope isolationScope = KsProcgenEntranceIsolationScope.DomainLocal)
    {
        configurations = [];
        issue = new("InvalidEntranceShorthand", "Use one nonempty alternative per line, bounded labels and persisted unique configuration IDs.");
        if (text.Length > 131_072 || configurationIds.Count is < 1 or > 64 || !Enum.IsDefined(mode) || !Enum.IsDefined(isolationScope) ||
            configurationIds.Any(id => !KsProcgenAreaNormalizer.ValidId(id)) ||
            configurationIds.Distinct(StringComparer.Ordinal).Count() != configurationIds.Count)
            return false;
        var lines = text.Trim().Split('\n');
        if (lines.Length != configurationIds.Count)
            return false;
        var imported = new List<KsProcgenEntranceConfigurationSpec>();
        for (var index = 0; index < lines.Length; index++)
        {
            var compact = new string(lines[index].Where(character => !char.IsWhiteSpace(character)).ToArray());
            var segments = compact.Split(';');
            if (segments.Length is < 1 or > 1024)
                return false;
            var groups = new List<KsProcgenEntranceGroupSpec>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var segment in segments)
            {
                var ports = segment.Split('-');
                if (ports.Length > 1024 || ports.Any(port => port.Length is < 1 or > 128 ||
                    port.Any(character => !(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')) || !seen.Add(port)) ||
                    seen.Count > 1024)
                    return false;
                var orderedPorts = ports.OrderBy(port => port, StringComparer.Ordinal).ToList();
                groups.Add(new() { Id = GroupId(orderedPorts), Ports = orderedPorts });
            }
            imported.Add(new() { Id = configurationIds[index], Mode = mode, IsolationScope = isolationScope,
                Groups = groups.OrderBy(group => group.Id, StringComparer.Ordinal).ToList() });
        }
        configurations = imported.AsReadOnly();
        issue = null;
        return true;
    }

    private static string GroupId(IReadOnlyList<string> orderedPorts)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-entrance-shorthand-group-v1");
        hash.AddInt(orderedPorts.Count);
        foreach (var port in orderedPorts)
            hash.AddString(port);
        return $"KsGroup{hash.Value:X16}";
    }
}
