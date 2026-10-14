using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenRoomOptionalCore(string PackId, string CoreId, int Count);

public static partial class KsProcgenRoomCoreSearch
{
    /// <summary>
    /// Resolves complete packs, includes every required copy, and chooses one weighted optional
    /// core per pack by default. An explicit optional list (including empty) replaces that default.
    /// Never scatters members from different variants or silently drops an authored minimum.
    /// </summary>
    public static KsProcgenRoomCoreSearchResult SearchPacks(IPrototypeManager prototypeManager, IComponentFactory componentFactory,
        string roomId, IReadOnlyList<string> packIds, KsProcgenRoomAccessMask room, int seed,
        IReadOnlyList<KsProcgenRoomOptionalCore>? optionalCores = null,
        int maximumVariantProbes = 64, int maximumPlacementProbes = 4096,
        int maximumExpandedCells = 4096, int maximumLandingProbes = 4096)
    {
        KsProcgenRoomCoreSearchResult Fail(KsProcgenSupportedAccessStatus status, string code) => new()
            { Status = status, Issue = new(code, "Room entity packs could not resolve complete selected cores.") };
        if (prototypeManager == null || componentFactory == null || string.IsNullOrWhiteSpace(roomId) || roomId.Length > 512 ||
            packIds == null || packIds.Count is < 1 or > 16 ||
            packIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 256) ||
            packIds.Distinct(StringComparer.Ordinal).Count() != packIds.Count || optionalCores?.Count > 16 ||
            !KsProcgenSupportedAccessPlanner.ValidMask(room) || maximumVariantProbes is < 0 or > 64 ||
            maximumPlacementProbes is < 0 or > 65_536 || maximumExpandedCells is < 0 or > 65_536 ||
            maximumLandingProbes is < 0 or > 65_536 ||
            optionalCores?.Any(core => core == null || string.IsNullOrWhiteSpace(core.PackId) ||
                string.IsNullOrWhiteSpace(core.CoreId) || core.Count is < 1 or > 8) == true)
            return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidRoomPackSelection");
        var resolved = new Dictionary<string, IReadOnlyList<KsProcgenResolvedEntityCore>>(StringComparer.Ordinal);
        foreach (var packId in packIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!prototypeManager.TryIndex<KsProcgenEntityPackPrototype>(packId, out var pack))
                return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "UnknownRoomEntityPack");
            if (!KsProcgenAssemblyCompiler.TryResolvePack(prototypeManager, pack, out var cores, out var issue))
                return Fail(KsProcgenSupportedAccessStatus.InvalidInput, issue!.Code);
            resolved.Add(packId, cores);
        }
        var optional = new Dictionary<(string PackId, string CoreId), int>();
        if (optionalCores != null)
        {
            foreach (var selection in optionalCores)
            {
                if (!resolved.TryGetValue(selection.PackId, out var cores) ||
                    !cores.Any(core => core.Id == selection.CoreId && core.MinimumCount == 0) ||
                    !optional.TryAdd((selection.PackId, selection.CoreId), selection.Count))
                    return Fail(KsProcgenSupportedAccessStatus.InvalidInput, "InvalidRoomOptionalCore");
            }
        }
        else
        {
            foreach (var (packId, cores) in resolved)
            {
                var selected = KsProcgenCoreSelector.Order(cores.Where(core => core.MinimumCount == 0 && core.Weight > 0f).ToArray(),
                    seed, roomId, packId, 0).FirstOrDefault();
                if (selected != null) optional.Add((packId, selected.Id), 1);
            }
        }
        var instances = new List<KsProcgenRoomCoreSelection>();
        foreach (var (packId, cores) in resolved)
        foreach (var core in cores)
        {
            var count = core.MinimumCount > 0 ? core.MinimumCount : optional.GetValueOrDefault((packId, core.Id));
            if (count > 16 - instances.Count)
                return Fail(KsProcgenSupportedAccessStatus.BudgetExceeded, "RoomCoreInstanceBudget");
            for (var copy = 0; copy < count; copy++) instances.Add(new(packId, core, copy));
        }
        if (instances.Count == 0) return Fail(KsProcgenSupportedAccessStatus.Rejected, "NoRoomCoresSelected");
        return Search(prototypeManager, componentFactory, roomId, instances, room, seed,
            maximumVariantProbes: maximumVariantProbes, maximumPlacementProbes: maximumPlacementProbes,
            maximumExpandedCells: maximumExpandedCells, maximumLandingProbes: maximumLandingProbes);
    }
}
