using System.Linq;
using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Item;
using Content.Shared.Placeable;
using Content.Shared.Rotatable;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenFixtureDeclaration(string Id, bool Hard, int Layer, int Mask);

public sealed record KsProcgenSlotFilterDeclaration(
    bool RequireAll, IReadOnlyList<string> Components, IReadOnlyList<string> Tags, IReadOnlyList<string> Sizes);

/// <summary>Contact-driven item tracking, not a general surface mounting or named-slot contract.</summary>
public sealed record KsProcgenSurfaceTrackerDeclaration(
    uint MaximumTrackedEntities, int InitiallyTrackedEntities, KsProcgenSlotFilterDeclaration? Whitelist)
{
    public int? DeclaredCapacity => MaximumTrackedEntities is > 0 and <= int.MaxValue ?
        (int) MaximumTrackedEntities : null;
}

public enum KsProcgenDeclaredContainerKind : byte
{
    Unknown,
    Container,
    Slot,
}

public sealed record KsProcgenContainerDeclaration(
    string Id, KsProcgenDeclaredContainerKind Kind, bool HasContainerDeclaration,
    bool HasItemSlotDeclaration, bool Locked, string? StartingItem,
    int PrototypeContentCount, int ExpectedContentCount,
    KsProcgenSlotFilterDeclaration? Whitelist, KsProcgenSlotFilterDeclaration? Blacklist)
{
    public int? DeclaredCapacity => Kind == KsProcgenDeclaredContainerKind.Slot ? 1 : null;
}

/// <summary>
/// Detached inherited prototype declarations. Runtime initialization, collision shapes, interaction
/// direction, mounting/anchoring and insertion events are not validated by this snapshot.
/// </summary>
public sealed record KsProcgenPrototypeCapabilities(
    string PrototypeId, bool HasSurface, bool SurfaceInitiallyEnabled, bool SurfaceCentered,
    Vector2 SurfaceOffset, bool HasItem, bool InitiallyAnchored, bool HasRotationVerb,
    bool RotationWhileAnchored, double RotationIncrementRadians,
    IReadOnlyList<KsProcgenFixtureDeclaration> Fixtures, IReadOnlyList<KsProcgenContainerDeclaration> Containers)
{
    public KsProcgenSurfaceTrackerDeclaration? SurfaceTracker { get; init; }
    public ulong DeclarationHash => KsProcgenPrototypeCapabilityInspector.Hash(this);
    public bool EnginePlacementVerified => false;
}

public sealed record KsProcgenMemberCapabilityDeclaration(string MemberId, KsProcgenPrototypeCapabilities Capabilities);

public sealed record KsProcgenAssemblyCapabilityWitness(
    string RelationId, string SubjectMemberId, string TargetMemberId, KsProcgenRelationKind Kind,
    KsProcgenSupportInspection Inspection);

public sealed record KsProcgenAssemblyCapabilityReport(
    IReadOnlyList<KsProcgenMemberCapabilityDeclaration> Members,
    IReadOnlyList<KsProcgenAssemblyCapabilityWitness> SupportRelations);

public static class KsProcgenPrototypeCapabilityInspector
{
    public static bool TryInspectAssembly(IPrototypeManager prototypeManager, IComponentFactory componentFactory,
        KsProcgenResolvedAssembly assembly, out KsProcgenAssemblyCapabilityReport? report, out KsProcgenIssue? issue)
    {
        report = null;
        issue = null;
        if (assembly.Members.Count is < 1 or > 64 || assembly.Relations.Count > 256 ||
            assembly.Members.Select(member => member.Id).Distinct(StringComparer.Ordinal).Count() != assembly.Members.Count)
        {
            issue = new KsProcgenIssue("InvalidCapabilityAssembly", "Capability inspection requires a bounded normalized assembly.");
            return false;
        }
        var prototypes = new Dictionary<string, KsProcgenPrototypeCapabilities>(StringComparer.Ordinal);
        var members = new Dictionary<string, KsProcgenPrototypeCapabilities>(StringComparer.Ordinal);
        foreach (var member in assembly.Members.OrderBy(member => member.Id, StringComparer.Ordinal))
        {
            if (!prototypes.TryGetValue(member.Entry.Entity, out var capabilities))
            {
                if (!TryInspect(prototypeManager, componentFactory, member.Entry.Entity, out capabilities, out issue))
                    return false;
                prototypes.Add(member.Entry.Entity, capabilities!);
            }
            members.Add(member.Id, capabilities!);
        }
        var witnesses = new List<KsProcgenAssemblyCapabilityWitness>();
        foreach (var relation in assembly.Relations.Where(relation => relation.Kind is
                     KsProcgenRelationKind.OnSurface or KsProcgenRelationKind.InContainer)
                     .OrderBy(relation => relation.Id, StringComparer.Ordinal))
        {
            if (!members.TryGetValue(relation.Subject, out var subject) || relation.Target == null ||
                !members.TryGetValue(relation.Target, out var target))
            {
                issue = new KsProcgenIssue("InvalidCapabilityAssembly", "The support relation references a missing member.");
                return false;
            }
            witnesses.Add(new KsProcgenAssemblyCapabilityWitness(relation.Id, relation.Subject, relation.Target,
                relation.Kind, KsProcgenSupportCapabilityInspector.Inspect(subject, target, relation.Kind,
                    slotId: relation.Kind == KsProcgenRelationKind.OnSurface ? relation.Slot : relation.ContainerId)));
        }
        report = new KsProcgenAssemblyCapabilityReport(members.Select(pair =>
            new KsProcgenMemberCapabilityDeclaration(pair.Key, pair.Value)).ToList().AsReadOnly(), witnesses.AsReadOnly());
        return true;
    }

    public static bool TryInspect(IPrototypeManager prototypeManager, IComponentFactory componentFactory,
        string prototypeId, out KsProcgenPrototypeCapabilities? capabilities, out KsProcgenIssue? issue)
    {
        capabilities = null;
        issue = null;
        if (string.IsNullOrWhiteSpace(prototypeId) ||
            !prototypeManager.TryIndex<EntityPrototype>(prototypeId, out var prototype))
        {
            issue = new KsProcgenIssue("UnknownCapabilityPrototype", "The requested entity prototype cannot be inspected.");
            return false;
        }
        prototype.TryGetComponent<PlaceableSurfaceComponent>(out var surfaceComponent, componentFactory);
        prototype.TryGetComponent<ItemPlacerComponent>(out var surfaceTrackerComponent, componentFactory);
        prototype.TryGetComponent<FixturesComponent>(out var fixturesComponent, componentFactory);
        prototype.TryGetComponent<ContainerManagerComponent>(out var containersComponent, componentFactory);
        prototype.TryGetComponent<ItemSlotsComponent>(out var slotsComponent, componentFactory);
        prototype.TryGetComponent<RotatableComponent>(out var rotationComponent, componentFactory);
        prototype.TryGetComponent<TransformComponent>(out var transformComponent, componentFactory);
        // Read the slot declarations without invoking methods through the restricted component field.
        var declaredSlots = slotsComponent?.Slots;
        var fixtures = fixturesComponent?.Fixtures.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new KsProcgenFixtureDeclaration(pair.Key, pair.Value.Hard,
                pair.Value.CollisionLayer, pair.Value.CollisionMask)).ToArray() ?? [];
        var containerIds = (containersComponent?.Containers.Keys ?? Enumerable.Empty<string>())
            .Concat(slotsComponent?.Slots.Keys ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (fixtures.Length > 64 || containerIds.Length > 64)
        {
            issue = new KsProcgenIssue("PrototypeCapabilityBudget", "Prototype inspection exceeds the fixture/container budget.");
            return false;
        }
        var containers = new List<KsProcgenContainerDeclaration>();
        foreach (var id in containerIds)
        {
            BaseContainer? container = null;
            ItemSlot? itemSlot = null;
            containersComponent?.Containers.TryGetValue(id, out container);
            declaredSlots?.TryGetValue(id, out itemSlot);
            var kind = container switch
            {
                ContainerSlot => KsProcgenDeclaredContainerKind.Slot,
                Container => KsProcgenDeclaredContainerKind.Container,
                null when itemSlot != null => KsProcgenDeclaredContainerKind.Slot,
                _ => KsProcgenDeclaredContainerKind.Unknown,
            };
            containers.Add(new KsProcgenContainerDeclaration(id, kind, container != null, itemSlot != null,
                itemSlot?.Locked ?? false, itemSlot?.StartingItem?.ToString(),
                container?.ContainedEntities.Count ?? 0, container?.ExpectedEntities.Count ?? 0,
                CopyFilter(itemSlot?.Whitelist), CopyFilter(itemSlot?.Blacklist)));
        }
        capabilities = new KsProcgenPrototypeCapabilities(prototype.ID, surfaceComponent != null,
            surfaceComponent?.IsPlaceable ?? false, surfaceComponent?.PlaceCentered ?? false,
            surfaceComponent?.PositionOffset ?? Vector2.Zero,
            prototype.TryGetComponent<ItemComponent>(out _, componentFactory), transformComponent?.Anchored ?? false,
            rotationComponent != null, rotationComponent?.RotateWhileAnchored ?? false,
            rotationComponent?.Increment.Theta ?? 0.0,
            Array.AsReadOnly(fixtures), containers.AsReadOnly())
        {
            SurfaceTracker = surfaceTrackerComponent == null ? null : new KsProcgenSurfaceTrackerDeclaration(
                surfaceTrackerComponent.MaxEntities, surfaceTrackerComponent.PlacedEntities.Count,
                CopyFilter(surfaceTrackerComponent.Whitelist)),
        };
        return true;
    }

    private static KsProcgenSlotFilterDeclaration? CopyFilter(EntityWhitelist? filter) => filter == null ? null : new(
        filter.RequireAll, CopyNames(filter.Components ?? []),
        CopyNames(filter.Tags?.Select(tag => tag.ToString()) ?? []),
        CopyNames(filter.Sizes?.Select(size => size.ToString()) ?? []));

    private static IReadOnlyList<string> CopyNames(IEnumerable<string> names) =>
        Array.AsReadOnly(names.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray());

    public static ulong Hash(KsProcgenPrototypeCapabilities capabilities)
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-prototype-capabilities-v2");
        hash.AddString(capabilities.PrototypeId);
        hash.AddInt(capabilities.HasSurface ? 1 : 0);
        hash.AddInt(capabilities.SurfaceInitiallyEnabled ? 1 : 0);
        hash.AddInt(capabilities.SurfaceCentered ? 1 : 0);
        hash.AddInt(BitConverter.SingleToInt32Bits(capabilities.SurfaceOffset.X));
        hash.AddInt(BitConverter.SingleToInt32Bits(capabilities.SurfaceOffset.Y));
        hash.AddInt(capabilities.SurfaceTracker == null ? 0 : 1);
        if (capabilities.SurfaceTracker is { } tracker)
        {
            hash.AddInt(unchecked((int) tracker.MaximumTrackedEntities));
            hash.AddInt(tracker.InitiallyTrackedEntities);
            AddFilter(ref hash, tracker.Whitelist);
        }
        hash.AddInt(capabilities.HasItem ? 1 : 0);
        hash.AddInt(capabilities.InitiallyAnchored ? 1 : 0);
        hash.AddInt(capabilities.HasRotationVerb ? 1 : 0);
        hash.AddInt(capabilities.RotationWhileAnchored ? 1 : 0);
        var rotationBits = BitConverter.DoubleToInt64Bits(capabilities.RotationIncrementRadians);
        hash.AddInt(unchecked((int) rotationBits));
        hash.AddInt(unchecked((int) (rotationBits >> 32)));
        hash.AddInt(capabilities.Fixtures.Count);
        foreach (var fixture in capabilities.Fixtures.OrderBy(fixture => fixture.Id, StringComparer.Ordinal))
        {
            hash.AddString(fixture.Id);
            hash.AddInt(fixture.Hard ? 1 : 0);
            hash.AddInt(fixture.Layer);
            hash.AddInt(fixture.Mask);
        }
        hash.AddInt(capabilities.Containers.Count);
        foreach (var container in capabilities.Containers.OrderBy(container => container.Id, StringComparer.Ordinal))
        {
            hash.AddString(container.Id);
            hash.AddInt((int) container.Kind);
            hash.AddInt(container.HasContainerDeclaration ? 1 : 0);
            hash.AddInt(container.HasItemSlotDeclaration ? 1 : 0);
            hash.AddInt(container.Locked ? 1 : 0);
            hash.AddString(container.StartingItem ?? string.Empty);
            hash.AddInt(container.PrototypeContentCount);
            hash.AddInt(container.ExpectedContentCount);
            AddFilter(ref hash, container.Whitelist);
            AddFilter(ref hash, container.Blacklist);
        }
        return hash.Value;
    }

    private static void AddFilter(ref KsProcgenStableHash hash, KsProcgenSlotFilterDeclaration? filter)
    {
        hash.AddInt(filter == null ? 0 : 1);
        if (filter == null)
            return;
        hash.AddInt(filter.RequireAll ? 1 : 0);
        foreach (var names in new[] { filter.Components, filter.Tags, filter.Sizes })
        {
            var sorted = names.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            hash.AddInt(sorted.Length);
            foreach (var name in sorted)
                hash.AddString(name);
        }
    }
}

public enum KsProcgenSupportInspectionState : byte
{
    Rejected,
    Unverified,
}

public sealed record KsProcgenSupportInspection(
    KsProcgenSupportInspectionState State, string Reason, bool HardCollisionCandidate,
    int? DeclaredCapacity, ulong SubjectDeclarationHash, ulong TargetDeclarationHash);

public static class KsProcgenSupportCapabilityInspector
{
    public static KsProcgenSupportInspection Inspect(KsProcgenPrototypeCapabilities subject,
        KsProcgenPrototypeCapabilities target, KsProcgenRelationKind kind, string? slotId = null)
    {
        var collision = subject.Fixtures.Any(first => first.Hard && target.Fixtures.Any(second => second.Hard &&
            ((first.Mask & second.Layer) != 0 || (second.Mask & first.Layer) != 0)));
        KsProcgenSupportInspection Result(KsProcgenSupportInspectionState state, string reason, int? capacity = null) =>
            new(state, reason, collision, capacity, subject.DeclarationHash, target.DeclarationHash);
        if (kind == KsProcgenRelationKind.OnSurface)
        {
            if (!target.HasSurface)
                return Result(KsProcgenSupportInspectionState.Rejected, "MissingPlaceableSurface");
            if (!target.SurfaceInitiallyEnabled)
                return Result(KsProcgenSupportInspectionState.Rejected, "SurfaceInitiallyDisabled");
            var capacity = slotId == null ? target.SurfaceTracker?.DeclaredCapacity : null;
            if (capacity != null && target.SurfaceTracker!.InitiallyTrackedEntities >= capacity)
                return Result(KsProcgenSupportInspectionState.Rejected, "SurfaceTrackerInitiallyFull", capacity: capacity);
            return Result(KsProcgenSupportInspectionState.Unverified, slotId != null ? "NamedSurfaceSlotUnverified" :
                collision ? "SurfaceHardCollisionCandidate" : target.SurfaceTracker != null ?
                    "SurfaceTrackerPlacementUnverified" : "SurfaceCapacityAndPlacementUnverified", capacity: capacity);
        }
        if (kind != KsProcgenRelationKind.InContainer)
            return Result(KsProcgenSupportInspectionState.Unverified, "CapabilityRuleNotInspected");
        if (string.IsNullOrWhiteSpace(slotId))
            return Result(KsProcgenSupportInspectionState.Rejected, "NamedContainerRequired");
        var container = target.Containers.FirstOrDefault(candidate => candidate.Id == slotId);
        if (container == null)
            return Result(KsProcgenSupportInspectionState.Rejected, "MissingDeclaredContainer");
        if (container.HasItemSlotDeclaration && container.Kind != KsProcgenDeclaredContainerKind.Slot)
            return Result(KsProcgenSupportInspectionState.Rejected, "ItemSlotContainerTypeMismatch", capacity: container.DeclaredCapacity);
        if (container.Locked)
            return Result(KsProcgenSupportInspectionState.Rejected, "ItemSlotInitiallyLocked", capacity: container.DeclaredCapacity);
        if (container.StartingItem != null || container.DeclaredCapacity == 1 &&
            (container.PrototypeContentCount > 0 || container.ExpectedContentCount > 0))
            return Result(KsProcgenSupportInspectionState.Rejected, "ItemSlotInitiallyReserved", capacity: container.DeclaredCapacity);
        return Result(KsProcgenSupportInspectionState.Unverified, "ContainerInsertionEventsUnverified", capacity: container.DeclaredCapacity);
    }
}
