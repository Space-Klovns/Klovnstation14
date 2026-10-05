using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

[DataDefinition]
public sealed partial class KsProcgenEntranceSpec
{
    [DataField(required: true)] public string PortId = string.Empty;
    [DataField(required: true)] public List<Vector2i> ThresholdCells = [];
    [DataField(required: true)] public Vector2i InwardNormal;
    [DataField] public bool OptionalSealable;
}

/// <summary>One blob-scoped entrance contract per request. Batch publication across blobs remains separate.</summary>
[DataDefinition]
public sealed partial class KsProcgenEntranceRequestSpec
{
    [DataField] public string Channel = "Default";
    [DataField(required: true)] public List<KsProcgenEntranceSpec> Entrances = [];
    [DataField] public List<KsProcgenEntranceConfigurationSpec> Configurations = [];
}

public static class KsProcgenEntranceRequestAdapter
{
    public static KsProcgenEntranceRequestSpec Copy(KsProcgenAreaBlob blob) => new()
    {
        Channel = blob.Channel,
        Entrances = blob.Entrances.Select(entrance => new KsProcgenEntranceSpec
        {
            PortId = entrance.PortId, ThresholdCells = entrance.ThresholdCells.ToList(),
            InwardNormal = entrance.InwardNormal, OptionalSealable = entrance.OptionalSealable,
        }).ToList(),
        Configurations = blob.ConnectionContract?.Configurations.Select(configuration => new KsProcgenEntranceConfigurationSpec
        {
            Id = configuration.Id, Weight = configuration.Weight, Mode = configuration.Mode, IsolationScope = configuration.IsolationScope,
            Groups = configuration.Groups.Select(group => new KsProcgenEntranceGroupSpec
            {
                Id = group.Id, Ports = group.Ports.ToList(), RootCells = group.RootCells.ToList(),
            }).ToList(), SealedPorts = configuration.SealedPorts.ToList(),
        }).ToList() ?? [],
    };

    internal static bool Normalize(KsProcgenRequest request, KsProcgenNormalizedShape shape,
        out IReadOnlyList<KsProcgenAreaEntrance> entrances, out KsProcgenEntranceConnectionContract? contract, out KsProcgenIssue? issue)
    {
        entrances = [];
        contract = null;
        issue = null;
        var domain = request.EntranceDomain;
        if (domain == null)
        {
            if (request.ConnectivityPolicy != KsProcgenConnectivityPolicy.DeclaredNetworks)
                return true;
            issue = new("MissingDeclaredEntranceDomain", "DeclaredNetworks needs a complete entrance contract and explicit group roots.");
            return false;
        }
        if (domain.Entrances == null || domain.Configurations == null || domain.Entrances.Count > 1024 ||
            domain.Entrances.Any(entrance => entrance == null || entrance.ThresholdCells == null))
        {
            issue = new("InvalidEntranceRequest", "Entrance request lists must be bounded and nonnull.");
            return false;
        }
        var blob = new KsProcgenAreaBlob(request.RequestId, domain.Channel,
            new(request.RequestId, request.Mode, request.GeometryMode, request.ConnectivityPolicy, null,
                request.Limits.MaxCells, request.Limits.MaxAbsoluteCoordinate), shape.TargetCells);
        var normalized = KsProcgenEntranceNormalizer.Normalize([blob], domain.Entrances.Select(entrance =>
            new KsProcgenEntranceMarker(entrance.PortId, domain.Channel, entrance.ThresholdCells, entrance.InwardNormal,
                BlobId: request.RequestId, OptionalSealable: entrance.OptionalSealable)).ToArray());
        if (normalized.Status is not (KsProcgenStatus.Success or KsProcgenStatus.NoOp))
        {
            issue = normalized.Issue;
            return false;
        }
        // No authored alternatives retains the all-connected default, rather than inventing separate groups.
        var configurations = domain.Configurations.Count == 0 ? new List<KsProcgenEntranceConfigurationSpec>
        {
            new() { Id = "KsDefaultAllConnected", Mode = KsProcgenEntranceConnectionMode.RequiredConnections,
                Groups = [new() { Id = "All", Ports = normalized.Entrances.Select(entrance => entrance.PortId).ToList() }] },
        } : domain.Configurations;
        var compiled = KsProcgenEntranceConnectionNormalizer.Normalize(request.RequestId, normalized.Entrances,
            configurations, request.ConnectivityPolicy);
        if (compiled.Status != KsProcgenStatus.Success)
        {
            issue = compiled.Issue;
            return false;
        }
        if (compiled.Configurations.SelectMany(configuration => configuration.Groups).SelectMany(group => group.RootCells)
            .Any(cell => !shape.ContainsTarget(cell)))
        {
            issue = new("EntranceRootOutsideDomain", "Every group root must belong to the exact target domain.");
            return false;
        }
        entrances = normalized.Entrances;
        contract = compiled;
        return true;
    }
}
