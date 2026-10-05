using System.Globalization;
using System.Linq;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Weighted ordering without replacement. Each core has an independent seeded exponential key;
/// placement decides feasibility, and mandatory minima remain separate obligations.
/// </summary>
public static class KsProcgenCoreSelector
{
    public static IReadOnlyList<KsProcgenResolvedEntityCore> Order(
        IReadOnlyList<KsProcgenResolvedEntityCore> cores, int seed, string regionId, string packId, int drawIndex)
    {
        if (cores.Count > 64 || string.IsNullOrWhiteSpace(regionId) || string.IsNullOrWhiteSpace(packId) ||
            drawIndex < 0 || cores.Any(core => string.IsNullOrWhiteSpace(core.Id) ||
                !float.IsFinite(core.Weight) || core.Weight < 0f) ||
            cores.Select(core => core.Id).Distinct(StringComparer.Ordinal).Count() != cores.Count)
            throw new ArgumentException("Core selection requires bounded, unique cores and finite nonnegative weights.");
        return cores.Where(core => core.Weight > 0f).Select(core =>
            {
                var hash = KsProcgenStableHash.Create();
                hash.AddString(regionId);
                hash.AddString(packId);
                hash.AddInt(drawIndex);
                hash.AddString(core.Id);
                var random = KsProcgenRandom.ForStage(seed, "furnishing/core-order",
                    hash.Value.ToString(CultureInfo.InvariantCulture));
                // Exactly representable denominator keeps the sample strictly between zero and one.
                var unit = (double) ((random.NextUInt64() >> 12) + 1) / 4_503_599_627_370_497.0;
                return (Core: core, Rank: -Math.Log(unit) / (double) core.Weight);
            }).OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.Core.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Core).ToArray();
    }
}
