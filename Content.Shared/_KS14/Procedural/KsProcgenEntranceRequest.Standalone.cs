using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public static partial class KsProcgenEntranceRequestAdapter
{
    /// <summary>Rooted networks without fabricated entrance thresholds or outside host tiles.</summary>
    private static bool TryStandaloneConfigurations(KsProcgenRequest request, KsProcgenNormalizedShape shape,
        out List<KsProcgenEntranceConfigurationSpec> configurations, out KsProcgenIssue? issue)
    {
        configurations = [];
        issue = null;
        if (request.ConnectivityPolicy == KsProcgenConnectivityPolicy.DeclaredNetworks)
        {
            issue = new("MissingDeclaredNetworkConfigurations", "DeclaredNetworks requires explicit groups and roots, including when no entrances are authored.");
            return false;
        }
        if (request.RootCells.Count > 1024)
        {
            issue = new("EntranceRootBudget", "Standalone request roots exceed the existing connection-contract allowance.");
            return false;
        }
        var components = shape.TargetComponents();
        if (components.Count is < 1 or > 1024)
        {
            issue = new("StandaloneRootBudget", "A standalone entrance domain needs one to 1024 bounded rooted target islands.");
            return false;
        }
        var groups = new List<KsProcgenEntranceGroupSpec>();
        var requestedRootsByCell = new Dictionary<Vector2i, List<Vector2i>>();
        foreach (var root in request.RootCells)
        {
            if (!requestedRootsByCell.TryGetValue(root, out var rootsAtCell))
                requestedRootsByCell.Add(root, rootsAtCell = new List<Vector2i>());
            rootsAtCell.Add(root);
        }
        foreach (var component in components)
        {
            var roots = new List<Vector2i>();
            foreach (var cell in component)
                if (requestedRootsByCell.TryGetValue(cell, out var rootsAtCell))
                    roots.AddRange(rootsAtCell);
            if (roots.Count == 0)
                roots.Add(component.FirstOrDefault(shape.CanWrite, defaultValue: component[0]));
            groups.Add(new() { Id = $"Root{groups.Count:D4}", Ports = [], RootCells = roots });
        }
        if (request.ConnectivityPolicy == KsProcgenConnectivityPolicy.SingleNetwork)
            groups = new List<KsProcgenEntranceGroupSpec>
                { new() { Id = "All", Ports = [], RootCells = groups.SelectMany(group => group.RootCells).ToList() } };
        configurations = new List<KsProcgenEntranceConfigurationSpec>
        {
            new()
            {
                Id = "KsDefaultStandalone", Mode = KsProcgenEntranceConnectionMode.ExactComponents,
                IsolationScope = KsProcgenEntranceIsolationScope.DomainLocal, Groups = groups,
            },
        };
        return true;
    }
}
