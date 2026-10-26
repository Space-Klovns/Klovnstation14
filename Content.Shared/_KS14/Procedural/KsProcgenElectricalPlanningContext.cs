using System.Collections.Frozen;
using System.Linq;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenElectricalPrototypeFact(string PrototypeId, bool Anchored, bool NoLocalRotation,
    bool Powered, bool HasReceiver, int ReceptionRange, float? NominalWatts,
    IReadOnlyList<Vector2i>? Footprint = null, bool WallMounted = false);
public sealed record KsProcgenElectricalCable(string PrototypeId, Vector2i Cell, KsProcgenElectricalVoltage Voltage);
public sealed record KsProcgenElectricalDevicePlacement(string Id, string RegionId, string PrototypeId,
    Vector2i Cell, int QuarterTurns, bool Substation, bool WallMounted = false);
public sealed record KsProcgenElectricalPlacement(string Id, string RegionId, string PrototypeId,
    Vector2 Position, KsProcgenPlacementLayer Layer);
public sealed record KsProcgenElectricalConnectionProposal(string ConsumerId, string RegionId,
    string PrototypeId, Vector2 Position, Vector2i ProviderCell, float? NominalWatts, float? PeakWatts);
public sealed record KsProcgenElectricalOmission(string ConsumerId, string Code);

/// <summary>Complete speculative cable/device reservations. No provider membership or supply certificate.</summary>
public sealed class KsProcgenElectricalPlan
{
    internal KsProcgenElectricalPlan(KsProcgenElectricalProfile profile, ulong contextHash, ulong requestHash,
        IEnumerable<KsProcgenElectricalDevicePlacement> devices, IEnumerable<KsProcgenElectricalCable> cables,
        IEnumerable<KsProcgenElectricalConnectionProposal> consumers, IEnumerable<KsProcgenElectricalOmission> omissions, int work,
        KsProcgenElectricalServiceWitness? service = null)
    {
        Profile = profile; ContextHash = contextHash; RequestHash = requestHash; Work = work; Service = service;
        Devices = devices.OrderBy(item => item.Id, StringComparer.Ordinal).ToReadOnlyList();
        Cables = cables.OrderBy(item => item.Voltage).ThenBy(item => item.Cell.Y).ThenBy(item => item.Cell.X).ToReadOnlyList();
        Consumers = consumers.OrderBy(item => item.ConsumerId, StringComparer.Ordinal).ToReadOnlyList();
        Omissions = omissions.Distinct().OrderBy(item => item.ConsumerId, StringComparer.Ordinal).ThenBy(item => item.Code, StringComparer.Ordinal).ToReadOnlyList();
        var hash = KsProcgenStableHash.Create(); hash.AddString("ks-electrical-plan-v2");
        hash.AddString(profile.SemanticHash.ToString(System.Globalization.CultureInfo.InvariantCulture));
        hash.AddString(contextHash.ToString(System.Globalization.CultureInfo.InvariantCulture));
        hash.AddString(requestHash.ToString(System.Globalization.CultureInfo.InvariantCulture)); hash.AddInt(Devices.Count);
        foreach (var device in Devices) { hash.AddString(device.Id); hash.AddString(device.RegionId); hash.AddString(device.PrototypeId);
            hash.AddInt(device.Cell.X); hash.AddInt(device.Cell.Y); hash.AddInt(device.QuarterTurns); hash.AddInt(device.Substation ? 1 : 0); hash.AddInt(device.WallMounted ? 1 : 0); }
        hash.AddInt(Cables.Count); foreach (var cable in Cables) { hash.AddString(cable.PrototypeId); hash.AddInt((int) cable.Voltage); hash.AddInt(cable.Cell.X); hash.AddInt(cable.Cell.Y); }
        hash.AddInt(Consumers.Count);
        foreach (var consumer in Consumers) { hash.AddString(consumer.ConsumerId); hash.AddString(consumer.RegionId); hash.AddString(consumer.PrototypeId);
            hash.AddInt(BitConverter.SingleToInt32Bits(consumer.Position.X)); hash.AddInt(BitConverter.SingleToInt32Bits(consumer.Position.Y));
            hash.AddInt(consumer.ProviderCell.X); hash.AddInt(consumer.ProviderCell.Y); hash.AddInt(consumer.NominalWatts.HasValue ? 1 : 0);
            if (consumer.NominalWatts.HasValue) hash.AddInt(BitConverter.SingleToInt32Bits(consumer.NominalWatts.Value));
            hash.AddInt(consumer.PeakWatts.HasValue ? 1 : 0); if (consumer.PeakWatts.HasValue) hash.AddInt(BitConverter.SingleToInt32Bits(consumer.PeakWatts.Value)); }
        hash.AddInt(Omissions.Count); foreach (var omission in Omissions) { hash.AddString(omission.ConsumerId); hash.AddString(omission.Code); }
        if (service != null) hash.AddString(service.DeclarationHash.ToString(System.Globalization.CultureInfo.InvariantCulture));
        PlanHash = hash.Value;
    }
    public KsProcgenElectricalProfile Profile { get; }
    public ulong ContextHash { get; }
    public ulong RequestHash { get; }
    public ulong PlanHash { get; }
    public int Work { get; }
    public IReadOnlyList<KsProcgenElectricalDevicePlacement> Devices { get; }
    public IReadOnlyList<KsProcgenElectricalCable> Cables { get; }
    public IReadOnlyList<KsProcgenElectricalConnectionProposal> Consumers { get; }
    public IReadOnlyList<KsProcgenElectricalOmission> Omissions { get; }
    public KsProcgenElectricalServiceWitness? Service { get; }
    public bool ElectricalTopologyVerified => false;
    public bool ElectricalSupplyVerified => false;
}

/// <summary>Detached inspected declarations for the owned candidate. Not live source or load evidence.</summary>
public sealed partial class KsProcgenElectricalPlanningContext
{
    private readonly FrozenDictionary<string, KsProcgenElectricalPrototypeFact> _facts;
    public KsProcgenElectricalProfile Profile { get; }
    public ulong DeclarationHash { get; }
    public int ProviderRange { get; }
    public KsProcgenThemeCatalog? UtilityContent { get; }
    /// <summary>Soft space preference: converter footprint, operating approach and loose contents.</summary>
    internal int PreferredUtilityInteriorCells => UtilityContent is { EntityPacks.Count: > 0 } content &&
        content.Theme.Goals.FurnishingDensity > 0f
        ? 2 + Profile.Substations.Where(device => device.Weight > 0f).Select(device =>
            _facts[device.Prototype].WallMounted ? 0 : _facts[device.Prototype].Footprint!.Count).DefaultIfEmpty(1).Min()
        : 0;

    private KsProcgenElectricalPlanningContext(KsProcgenElectricalProfile profile,
        IEnumerable<KsProcgenElectricalPrototypeFact> facts, string prototypeHash, int providerRange, KsProcgenThemeCatalog? utilityContent)
    {
        Profile = profile; ProviderRange = providerRange; UtilityContent = utilityContent;
        _facts = facts.Select(item => item with { Footprint = KsProcgenGeometry.SortCells(item.Footprint ?? new[] { Vector2i.Zero }) })
            .ToFrozenDictionary(item => item.PrototypeId, StringComparer.Ordinal);
        var hash = KsProcgenStableHash.Create(); hash.AddString("ks-electrical-context-v2"); hash.AddString(prototypeHash);
        hash.AddString(profile.SemanticHash.ToString(System.Globalization.CultureInfo.InvariantCulture)); hash.AddInt(providerRange);
        foreach (var fact in _facts.Values.OrderBy(item => item.PrototypeId, StringComparer.Ordinal))
        { hash.AddString(fact.PrototypeId); hash.AddInt(fact.WallMounted ? 1 : 0); hash.AddInt(fact.Anchored ? 1 : 0); hash.AddInt(fact.NoLocalRotation ? 1 : 0);
            hash.AddInt(fact.Powered ? 1 : 0); hash.AddInt(fact.HasReceiver ? 1 : 0); hash.AddInt(fact.ReceptionRange);
            hash.AddInt(fact.NominalWatts.HasValue ? 1 : 0); if (fact.NominalWatts.HasValue) hash.AddInt(BitConverter.SingleToInt32Bits(fact.NominalWatts.Value));
            hash.AddInt(fact.Footprint!.Count); foreach (var cell in fact.Footprint) { hash.AddInt(cell.X); hash.AddInt(cell.Y); } }
        if (utilityContent != null)
        {
            hash.AddString(utilityContent.DeclarationHash.ToString(System.Globalization.CultureInfo.InvariantCulture));
            hash.AddInt(KsProcgenLowVoltageRouter.AlgorithmVersion);
            hash.AddInt(KsProcgenUtilityRoomAllocator.AlgorithmVersion);
        }
        DeclarationHash = hash.Value;
    }

    public static bool TryCreate(KsProcgenElectricalProfile profile, IReadOnlyList<KsProcgenElectricalPrototypeFact> facts,
        string prototypeHash, int providerRange, out KsProcgenElectricalPlanningContext? context, out KsProcgenIssue? issue,
        KsProcgenThemeCatalog? utilityContent = null)
    {
        context = null; issue = null;
        if (profile == null || facts == null || facts.Count > 4096 || string.IsNullOrWhiteSpace(prototypeHash) || prototypeHash.Length > 128 ||
            providerRange is < 0 or > 64 || facts.Any(item => item == null || string.IsNullOrWhiteSpace(item.PrototypeId) || item.PrototypeId.Length > 256 ||
                item.ReceptionRange is < 0 or > 64 || item.NominalWatts is { } watts && (!float.IsFinite(watts) || watts < 0f) ||
                item.Footprint is { } footprint && (footprint.Count is < 1 or > 16 || footprint.Distinct().Count() != footprint.Count || !footprint.Contains(Vector2i.Zero) ||
                    footprint.Any(cell => Math.Abs((long) cell.X) > 8 || Math.Abs((long) cell.Y) > 8))) ||
            facts.Select(item => item.PrototypeId).Distinct(StringComparer.Ordinal).Count() != facts.Count)
        { issue = new("InvalidElectricalPlanningContext", "Electrical context needs bounded detached prototype facts."); return false; }
        if (profile.Distribution is { } distribution && (utilityContent == null || utilityContent.Theme.Id != distribution.ClosetTheme ||
                utilityContent.Theme.Electrical != null || utilityContent.CandidateEntityIds().Any(id => !facts.Any(fact => fact.PrototypeId == id))) ||
            profile.Distribution == null && utilityContent != null)
        { issue = new("ElectricalUtilityRoomContextRequired", "Distribution needs its matching frozen closet catalog and every native candidate; nested electrical overrides are unsupported."); return false; }
        if (profile.Mode != KsProcgenElectricalMode.AutoDistribute || profile.SupplyRequirement != KsProcgenElectricalSupply.WiredOnly ||
            profile.CircuitPolicy != KsProcgenElectricalCircuitPolicy.Shared || profile.Interfaces.Count != 0 ||
            new[] { profile.HighVoltageCable, profile.MediumVoltageCable, profile.LowVoltageCable }
                .Any(id => !facts.Any(item => item.PrototypeId == id && item.Anchored)) ||
            profile.Apcs.Where(item => item.Weight > 0f).Any(choice => !facts.Any(item => item.PrototypeId == choice.Prototype && item.WallMounted)) ||
            profile.Substations.Count(item => item.Weight > 0f) is < 1 or > 16 || profile.Apcs.Count(item => item.Weight > 0f) is < 1 or > 16 ||
            profile.Substations.Where(item => item.Weight > 0f).Concat(profile.Apcs.Where(item => item.Weight > 0f)).Any(choice =>
                !facts.Any(item => item.PrototypeId == choice.Prototype && item.Anchored)))
        { issue = new("ElectricalContextCapabilityUnsupported", "Owned distribution currently needs inspected anchored converters, shared wired-only circuits and no existing source interfaces."); return false; }
        context = new(profile, facts, prototypeHash, providerRange, utilityContent); return true;
    }

    internal bool TryRequiredCores(KsProcgenThemedRegion region, int seed, IReadOnlySet<Vector2i> allowedCells,
        out IReadOnlyList<KsProcgenRoomCoreSelection> selections, out KsProcgenIssue? issue, bool? substationRole = null)
    {
        var result = new List<KsProcgenRoomCoreSelection>(); issue = null; selections = [];
        foreach (var substation in new[] { true, false })
        {
            if (substationRole.HasValue && substationRole.Value != substation) continue;
            var role = substation ? "Substation" : "Apc"; var choices = substation ? Profile.Substations : Profile.Apcs;
            var variants = new List<KsProcgenResolvedEntityCore>();
            foreach (var choice in choices.Where(item => item.Weight > 0f))
            {
                var fact = _facts[choice.Prototype];
                if (!KsProcgenAssemblyCompiler.TryCompileVariant("Electrical" + role, new()
                {
                    Id = "V" + variants.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), AnchorMember = "Device",
                    Members = new() { new() { Id = "Device", Entity = choice.Prototype, Role = KsProcgenEntityRole.Equipment,
                        RequiresInteractionApproach = true, RotationMode = KsProcgenMemberRotation.Independent, Footprint = fact.Footprint!.ToList(),
                        AllowedQuarterTurns = fact.NoLocalRotation ? new() { 0 } : new() { 0, 1, 2, 3 } } },
                }, new Dictionary<string, string>(), id => _facts.ContainsKey(id), out var assembly, out issue)) return false;
                variants.Add(new(choice.Prototype, choice.Weight, 1, new[] { assembly! }));
            }
            var ordered = KsProcgenCoreSelector.Order(variants, seed, region.Id, "Electrical" + role, 0);
            if (ordered.Count == 0) { issue = new("MissingElectricalConverterChoice", "A converter role needs an enabled captured variant."); return false; }
            result.Add(new("Electrical", new(role, 1f, 1, ordered.Select(item => item.Variants[0]).ToReadOnlyList()), 0)
                { Origin = KsProcgenRoomMemberOrigin.Infrastructure, AllowedFloorCells = allowedCells });
        }
        selections = result.ToReadOnlyList(); return true;
    }
}
