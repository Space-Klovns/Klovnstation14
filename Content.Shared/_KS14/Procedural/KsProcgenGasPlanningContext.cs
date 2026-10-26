using System.Collections.Frozen;
using System.Linq;
using Content.Shared.Atmos;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>Detached initial defaults. Lifecycle callbacks and actual grid air remain unverified.</summary>
public sealed record KsProcgenGasPrototypeFact(string PrototypeId, bool Anchored,
    AtmosDirection BlockedDirections, bool NoAirWhenFullyBlocked, bool NoLocalRotation);

/// <summary>
/// Proposed gas topology for the exact nominated scene. This context retains values, never mutable
/// components or a prototype manager. Its identity names the captured engine declaration images.
/// </summary>
public sealed partial class KsProcgenGasPlanningContext
{
    private readonly FrozenDictionary<string, KsProcgenGasPrototypeFact> _facts;
    private static readonly (Vector2i Offset, AtmosDirection Direction)[] Cardinal =
    [
        (new(0, 1), AtmosDirection.North), (new(0, -1), AtmosDirection.South),
        (new(1, 0), AtmosDirection.East), (new(-1, 0), AtmosDirection.West),
    ];

    private KsProcgenGasPlanningContext(IEnumerable<KsProcgenGasPrototypeFact> facts, string prototypeHash)
    {
        _facts = facts.ToFrozenDictionary(fact => fact.PrototypeId, StringComparer.Ordinal);
        PrototypeDeclarationHash = prototypeHash;
        var hash = KsProcgenStableHash.Create(); hash.AddString("ks-gas-context-v2"); hash.AddString(prototypeHash);
        hash.AddInt(_facts.Count);
        foreach (var fact in _facts.Values.OrderBy(fact => fact.PrototypeId, StringComparer.Ordinal))
        {
            hash.AddString(fact.PrototypeId); hash.AddInt(fact.Anchored ? 1 : 0); hash.AddInt((int) fact.BlockedDirections);
            hash.AddInt(fact.NoAirWhenFullyBlocked ? 1 : 0); hash.AddInt(fact.NoLocalRotation ? 1 : 0);
        }
        DeclarationHash = hash.Value;
    }

    public string PrototypeDeclarationHash { get; }
    public ulong DeclarationHash { get; }

    public static bool TryCreate(IReadOnlyList<KsProcgenGasPrototypeFact> facts, string prototypeHash,
        out KsProcgenGasPlanningContext? context, out KsProcgenIssue? issue)
    {
        context = null; issue = null;
        if (facts == null || facts.Count > 4096 || string.IsNullOrWhiteSpace(prototypeHash) || prototypeHash.Length > 128 ||
            facts.Any(fact => fact == null || string.IsNullOrWhiteSpace(fact.PrototypeId) || fact.PrototypeId.Length > 512 ||
                (fact.BlockedDirections & ~AtmosDirection.All) != 0) ||
            facts.Select(fact => fact.PrototypeId).Distinct(StringComparer.Ordinal).Count() != facts.Count)
        { issue = new("InvalidGasPlanningContext", "Gas planning requires unique bounded initial prototype facts."); return false; }
        context = new(facts, prototypeHash);
        return true;
    }

    internal KsProcgenGasBindingResult Plan(KsProcgenRequestSnapshot request, KsProcgenThemeCatalog content,
        KsProcgenGroupPartitionResult partition, KsProcgenThemeAssignmentResult themes,
        KsProcgenGroupWindowResult structure, IReadOnlyList<KsProcgenFurnishedRegion> furnishings,
        KsProcgenGroupLightingResult lighting, int maximumWork, IReadOnlyList<KsProcgenGasPlacement>? additionalPlacements = null)
    {
        var work = 0;
        KsProcgenGasBindingResult Fail(KsProcgenGasBindingStatus status, string code) =>
            new(status, new(code, "Proposed gas topology could not be compiled."), [], [], work, request.DeclarationHash,
                prototypeHash: PrototypeDeclarationHash, contextHash: DeclarationHash);
        var cells = partition.FloorCells.Concat(partition.WallCells).Concat(structure.RemainingWalls.Select(wall => wall.Cell))
            .Concat(structure.Windows.Select(window => window.Cell)).ToHashSet();
        var placements = new List<KsProcgenGasPlacement>();
        void Add(Vector2i cell, string prototypeId, int quarterTurns, KsProcgenPlacementLayer layer = KsProcgenPlacementLayer.Floor) =>
            placements.Add(new(prototypeId, cell, quarterTurns, layer));
        foreach (var wall in structure.RemainingWalls) Add(wall.Cell, wall.EntityId, 0);
        foreach (var window in structure.Windows) Add(window.Cell, window.EntityId, 0);
        var materials = structure.SourceHull!.InteriorMaterials!;
        var openings = materials.DoorOpenings.ToDictionary(opening => opening.Threshold);
        foreach (var door in materials.Doors)
        {
            if (!openings.TryGetValue(door.Cell, out var opening)) return Fail(KsProcgenGasBindingStatus.Rejected, "InvalidGasDoorOpening");
            Add(door.Cell, door.EntityId, opening.InsideApproach.X == opening.Threshold.X ? 0 : 1);
        }
        foreach (var region in furnishings)
        {
            if (region.Proposal.Layered is { } layered)
            {
                foreach (var pose in layered.Access.Poses!.Members)
                {
                    // Surface and container members are retained so the topology helper can apply layer semantics.
                    var cell = new Vector2i((int) Math.Floor((double) pose.Position.X), (int) Math.Floor((double) pose.Position.Y));
                    Add(cell, pose.Support.EntityPrototypeId, pose.QuarterTurns, layer: pose.Support.Layer);
                }
            }
            else
            foreach (var entity in region.Proposal.Entities) Add(entity.Cell, entity.EntityId, entity.QuarterTurns);
        }
        foreach (var region in lighting.Regions)
        foreach (var light in region.Proposal.Lights) Add(light.Cell, light.EntityId, 0);
        if (additionalPlacements != null) placements.AddRange(additionalPlacements);
        var topology = PlanTopology(KsProcgenGeometry.SortCells(cells), placements, maximumWork: maximumWork);
        work = topology.Work;
        if (topology.Status != KsProcgenGasBindingStatus.Bound)
            return Fail(topology.Status, topology.Issue!.Code);
        var rooms = themes.Regions.Select(region => new KsProcgenGasRoomTarget(region.Id, region.FloorCells,
            content.Theme.Atmosphere)).ToArray();
        var binding = KsProcgenGasBindings.Bind(request, rooms, topology.Domains, maximumWork: maximumWork - work,
            themeProfile: content.Theme.Atmosphere);
        return new(binding.Status, binding.Issue, binding.Domains, binding.Omissions, work + binding.Work,
            request.DeclarationHash, prototypeHash: PrototypeDeclarationHash, contextHash: DeclarationHash);
    }
}
