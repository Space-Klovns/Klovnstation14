using System.Linq;

namespace Content.Shared._KS14.Procedural;

internal static class KsProcgenEntranceConfigurationOrder
{
    internal static IReadOnlyList<KsProcgenEntranceConfiguration> Order(
        IReadOnlyList<KsProcgenEntranceConfiguration> configurations, int seed, string requestId) =>
        configurations.Where(configuration => configuration.Weight > 0f).Select(configuration =>
        {
            var random = KsProcgenRandom.ForStage(seed, "entrance/configuration-order", requestId + "/" + configuration.Id);
            var unit = (double) ((random.NextUInt64() >> 12) + 1) / 4_503_599_627_370_497.0;
            return (Configuration: configuration, Rank: -Math.Log(unit) / (double) configuration.Weight);
        }).OrderBy(item => item.Rank).ThenBy(item => item.Configuration.Id, StringComparer.Ordinal)
            .Select(item => item.Configuration).ToArray();
}
