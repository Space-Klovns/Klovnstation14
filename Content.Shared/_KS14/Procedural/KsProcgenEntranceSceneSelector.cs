using System.Globalization;
using System.Linq;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenEntranceSceneCandidate(string ConfigurationId, KsProcgenEntranceScene Scene);

public sealed class KsProcgenEntranceSceneSelection
{
    public KsProcgenEntranceSceneStatus Status { get; init; }
    public KsProcgenIssue? Issue { get; init; }
    public KsProcgenEntranceSceneResult? Accepted { get; init; }
    public int CandidateProbes { get; init; }
    public int ExpandedCells { get; init; }
    public ulong SelectionHash { get; init; }
}

/// <summary>Order and check complete detached scenes; no route claims from a rejected alternative survive.</summary>
public static class KsProcgenEntranceSceneSelector
{
    public static KsProcgenEntranceSceneSelection Select(KsProcgenRequest request,
        IReadOnlyList<KsProcgenEntranceSceneCandidate> candidates, int maximumCandidateProbes = 64, int maximumExpandedCells = 131_072)
    {
        var probes = 0;
        var expanded = 0;
        KsProcgenEntranceSceneSelection Fail(KsProcgenEntranceSceneStatus status, string code) => new()
        {
            Status = status, Issue = new(code, "No complete entrance scene was selected."), CandidateProbes = probes, ExpandedCells = expanded,
        };
        if (!KsProcgenGeometry.TryNormalize(request, out var shape, out var issue))
            return new() { Status = KsProcgenEntranceSceneStatus.InvalidInput, Issue = issue };
        if (shape!.EntranceContract == null || candidates.Count is < 1 or > 64 || maximumCandidateProbes is < 0 or > 64 ||
            maximumExpandedCells is < 0 or > 131_072 || candidates.Any(candidate => candidate == null || candidate.Scene == null ||
                !KsProcgenAreaNormalizer.ValidId(candidate.ConfigurationId)) ||
            candidates.Select(candidate => candidate.ConfigurationId).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "InvalidEntranceSceneCandidates");
        var configurations = shape.EntranceContract.Configurations.Where(configuration => configuration.Weight > 0f)
            .ToDictionary(configuration => configuration.Id, StringComparer.Ordinal);
        if (candidates.Any(candidate => !configurations.ContainsKey(candidate.ConfigurationId)))
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "UnknownEntranceSceneConfiguration");
        if (candidates.Count != configurations.Count)
            return Fail(KsProcgenEntranceSceneStatus.InvalidInput, "MissingEntranceSceneConfiguration");
        var byConfiguration = candidates.ToDictionary(candidate => candidate.ConfigurationId, StringComparer.Ordinal);
        foreach (var configuration in KsProcgenEntranceConfigurationOrder.Order(shape.EntranceContract.Configurations, request.Seed, request.RequestId))
        {
            if (probes >= maximumCandidateProbes)
                return Fail(KsProcgenEntranceSceneStatus.BudgetExceeded, "EntranceCandidateProbeBudget");
            probes++;
            var result = KsProcgenEntranceSceneAnalyzer.AnalyzeNormalized(shape, request.ConnectivityPolicy, request.RootCells,
                configuration.Id, byConfiguration[configuration.Id].Scene, maximumExpandedCells - expanded);
            expanded += result.ExpandedCells;
            if (result.Status == KsProcgenEntranceSceneStatus.Rejected)
                continue;
            if (result.Status != KsProcgenEntranceSceneStatus.Candidate)
                return new() { Status = result.Status, Issue = result.Issue, CandidateProbes = probes, ExpandedCells = expanded };
            var hash = KsProcgenStableHash.Create();
            hash.AddString("ks-procgen-entrance-selection-v1");
            hash.AddInt(request.Seed);
            hash.AddString(request.RequestId);
            hash.AddString(result.SceneHash.ToString(CultureInfo.InvariantCulture));
            return new()
            {
                Status = KsProcgenEntranceSceneStatus.Candidate, Accepted = result, CandidateProbes = probes,
                ExpandedCells = expanded, SelectionHash = hash.Value,
            };
        }
        return Fail(KsProcgenEntranceSceneStatus.Rejected, "NoFeasibleEntranceScene");
    }
}
