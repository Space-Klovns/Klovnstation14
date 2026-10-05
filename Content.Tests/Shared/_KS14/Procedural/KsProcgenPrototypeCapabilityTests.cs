using System.Collections.Generic;
using System.Numerics;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenPrototypeCapabilityTests
{
    [Test]
    public void SurfaceTrackingCapacityIsExplicitAndDoesNotCreateNamedSlots()
    {
        var entity = Entity() with { SurfaceTracker = new(4, 0, null) };
        var inspection = KsProcgenSupportCapabilityInspector.Inspect(Entity(), entity, KsProcgenRelationKind.OnSurface);
        Assert.That(inspection.DeclaredCapacity, Is.EqualTo(4));
        Assert.That(inspection.State, Is.EqualTo(KsProcgenSupportInspectionState.Unverified));
        Assert.That(inspection.Reason, Is.EqualTo("SurfaceTrackerPlacementUnverified"));
        Assert.That(KsProcgenSupportCapabilityInspector.Inspect(Entity(), entity, KsProcgenRelationKind.OnSurface,
            slotId: "Top").DeclaredCapacity, Is.Null);
        Assert.That(KsProcgenSupportCapabilityInspector.Inspect(Entity(), entity with { SurfaceTracker = new(4, 4, null) },
            KsProcgenRelationKind.OnSurface).Reason, Is.EqualTo("SurfaceTrackerInitiallyFull"));
        Assert.That((entity with { SurfaceTracker = new(0, 0, null) }).SurfaceTracker!.DeclaredCapacity, Is.Null,
            "Zero means unlimited tracking, not zero capacity or capacity one.");
        Assert.That((entity with { SurfaceTracker = new(uint.MaxValue, 0, null) }).SurfaceTracker!.DeclaredCapacity, Is.Null,
            "An unsigned capacity outside the structural integer range stays unbounded there.");
    }

    [Test]
    public void SurfaceTrackerHashPreservesCapacityOccupancyAndFilterRules()
    {
        var entity = Entity() with { SurfaceTracker = new(4, 0,
            new(false, ["Item", "Tag"], [], [])) };
        Assert.That((entity with { SurfaceTracker = new(4, 0, new(false, ["Tag", "Item", "Item"], [], [])) })
            .DeclarationHash, Is.EqualTo(entity.DeclarationHash));
        foreach (var changed in new[] { entity with { SurfaceTracker = null },
                     entity with { SurfaceTracker = new(0, 0, entity.SurfaceTracker!.Whitelist) },
                     entity with { SurfaceTracker = new(4, 1, entity.SurfaceTracker!.Whitelist) },
                     entity with { SurfaceTracker = new(4, 0, new(true, ["Item", "Tag"], [], [])) } })
            Assert.That(changed.DeclarationHash, Is.Not.EqualTo(entity.DeclarationHash));
    }

    [Test]
    public void CollisionCandidateUsesEitherDirectionAndIgnoresSoftFixtures()
    {
        var subject = Entity() with { Fixtures = [new("A", true, 1, 0)] };
        var target = Entity() with { Fixtures = [new("B", true, 0, 1)] };
        var inspected = KsProcgenSupportCapabilityInspector.Inspect(subject, target, KsProcgenRelationKind.OnSurface);
        Assert.That(inspected.HardCollisionCandidate, Is.True);
        Assert.That(inspected.State, Is.EqualTo(KsProcgenSupportInspectionState.Unverified));
        Assert.That(inspected.Reason, Is.EqualTo("SurfaceHardCollisionCandidate"));
        Assert.That(KsProcgenSupportCapabilityInspector.Inspect(target, subject, KsProcgenRelationKind.OnSurface)
            .HardCollisionCandidate, Is.True);
        Assert.That(KsProcgenSupportCapabilityInspector.Inspect(subject with
            { Fixtures = [new("A", false, 1, 0)] }, target, KsProcgenRelationKind.OnSurface)
            .HardCollisionCandidate, Is.False);
    }

    [Test]
    public void SurfaceDeclarationsDoNotInventCapacityOrPlacementProof()
    {
        var entity = Entity();
        var inspection = KsProcgenSupportCapabilityInspector.Inspect(entity, entity, KsProcgenRelationKind.OnSurface);
        Assert.That(inspection.State, Is.EqualTo(KsProcgenSupportInspectionState.Unverified));
        Assert.That(inspection.DeclaredCapacity, Is.Null);
        Assert.That(entity.EnginePlacementVerified, Is.False);
        Assert.That(KsProcgenSupportCapabilityInspector.Inspect(entity, entity, KsProcgenRelationKind.OnSurface,
            slotId: "Corner").Reason, Is.EqualTo("NamedSurfaceSlotUnverified"));
        Assert.That(KsProcgenSupportCapabilityInspector.Inspect(entity, entity with { HasSurface = false },
            KsProcgenRelationKind.OnSurface).State, Is.EqualTo(KsProcgenSupportInspectionState.Rejected));
        Assert.That(KsProcgenSupportCapabilityInspector.Inspect(entity, entity with { SurfaceInitiallyEnabled = false },
            KsProcgenRelationKind.OnSurface).Reason, Is.EqualTo("SurfaceInitiallyDisabled"));
    }

    [Test]
    public void NamedSlotReportsKnownCapacityButRequiresLiveInsertion()
    {
        var entity = Entity() with { Containers = [Slot()] };
        var inspection = KsProcgenSupportCapabilityInspector.Inspect(Entity(), entity,
            KsProcgenRelationKind.InContainer, slotId: "Device");
        Assert.That(inspection.State, Is.EqualTo(KsProcgenSupportInspectionState.Unverified));
        Assert.That(inspection.DeclaredCapacity, Is.EqualTo(1));
        Assert.That(inspection.Reason, Is.EqualTo("ContainerInsertionEventsUnverified"));
        Assert.That(KsProcgenSupportCapabilityInspector.Inspect(Entity(), entity,
            KsProcgenRelationKind.InContainer, slotId: "Missing").Reason, Is.EqualTo("MissingDeclaredContainer"));
        Assert.That(KsProcgenSupportCapabilityInspector.Inspect(Entity(), entity,
            KsProcgenRelationKind.InContainer).Reason, Is.EqualTo("NamedContainerRequired"));
    }

    [Test]
    public void LockedReservedOrWrongTypeSlotsAreRejected()
    {
        foreach (var slot in new[] { Slot() with { Locked = true }, Slot() with { StartingItem = "Existing" },
                     Slot() with { PrototypeContentCount = 1 }, Slot() with { ExpectedContentCount = 1 },
                     Slot() with { Kind = KsProcgenDeclaredContainerKind.Container } })
            Assert.That(KsProcgenSupportCapabilityInspector.Inspect(Entity(), Entity() with { Containers = [slot] },
                KsProcgenRelationKind.InContainer, slotId: "Device").State,
                Is.EqualTo(KsProcgenSupportInspectionState.Rejected));
        var generic = Entity() with { Containers = [Slot() with
            { Kind = KsProcgenDeclaredContainerKind.Container, HasItemSlotDeclaration = false }] };
        Assert.That(KsProcgenSupportCapabilityInspector.Inspect(Entity(), generic,
            KsProcgenRelationKind.InContainer, slotId: "Device").DeclaredCapacity, Is.Null);
    }

    [Test]
    public void HashIgnoresEnumerationOrderButTracksRelevantDeclarations()
    {
        var entity = Entity() with
        {
            Fixtures = [new("A", true, 1, 2), new("B", false, 4, 8)],
            Containers = [Slot(), Slot() with { Id = "Other" }],
        };
        Assert.That((entity with
        {
            Fixtures = [entity.Fixtures[1], entity.Fixtures[0]],
            Containers = [entity.Containers[1], entity.Containers[0]],
        }).DeclarationHash, Is.EqualTo(entity.DeclarationHash));
        foreach (var changed in new[] { entity with { SurfaceOffset = new Vector2(0.25f, 0f) },
                     entity with { RotationWhileAnchored = true }, entity with { RotationIncrementRadians = 1.0 },
                     entity with { Fixtures = [new("A", true, 1, 3), entity.Fixtures[1]] },
                     entity with { Containers = [Slot() with { Locked = true }, entity.Containers[1]] } })
            Assert.That(changed.DeclarationHash, Is.Not.EqualTo(entity.DeclarationHash));
    }

    [Test]
    public void FilterHashNormalizesSetsAndPreservesRuleMeaning()
    {
        var filter = new KsProcgenSlotFilterDeclaration(false, ["Item", "Tag"], ["B", "A"], ["Small"]);
        var entity = Entity() with { Containers = [Slot() with { Whitelist = filter }] };
        Assert.That((entity with { Containers = [Slot() with { Whitelist = filter with
            { Components = ["Tag", "Item"], Tags = ["A", "B", "A"] } }] }).DeclarationHash,
            Is.EqualTo(entity.DeclarationHash));
        Assert.That((entity with { Containers = [Slot() with { Whitelist = filter with { RequireAll = true } }] })
            .DeclarationHash, Is.Not.EqualTo(entity.DeclarationHash));
        Assert.That((entity with { Containers = [Slot() with { Blacklist = filter }] }).DeclarationHash,
            Is.Not.EqualTo(entity.DeclarationHash));
    }

    private static KsProcgenPrototypeCapabilities Entity() => new("Fixture", true, true, false,
        Vector2.Zero, false, false, false, false, 0.0, [], []);

    private static KsProcgenContainerDeclaration Slot() => new("Device", KsProcgenDeclaredContainerKind.Slot,
        true, true, false, null, 0, 0, null, null);
}
