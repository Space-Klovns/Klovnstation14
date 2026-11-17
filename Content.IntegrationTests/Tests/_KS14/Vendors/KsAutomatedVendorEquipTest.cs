#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared._RMC14.Vendors;
using Content.Shared.Clothing.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Vendors;

/// <summary>
///     The scenario vendors dress whoever takes a kit: clothing goes on, whatever the order it is listed in, and the
///         rest goes into their hands or bags rather than onto the floor.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedCMAutomatedVendorSystem))]
public sealed class KsAutomatedVendorEquipTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";

    // Exists only in the test prototypes below.
    private readonly EntProtoId _reversedVendor = "KsTestVendorReversed";

    // A kit listed back to front: the PDA before the webbing it used to have to follow, and the jumpsuit that the ID
    //      slot depends on last of all.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestVendorBundleReversed
  categories: [ HideSpawnMenu ]
  components:
  - type: CMVendorBundle
    bundle:
    - MedkitCombatFilled
    - Machete
    - Gauze
    - FlashlightSeclite
    - NanoplantNtPDA
    - ClothingBeltMilitaryWebbingERT
    - ClothingHandsGlovesCombat
    - ClothingShoesBootsLaceup
    - ClothingBackpackERTSecurity
    - ClothingUniformJumpsuitColorBlue

- type: entity
  parent: ColMarTechScenarioNanoplantNt
  id: KsTestVendorReversed
  components:
  - type: CMAutomatedVendor
    sections:
    - name: Reversed
      entries:
      - id: KsTestVendorBundleReversed
";

    [Test]
    public async Task KitOrderDoesNotDecideWhatIsWorn()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var entMan = server.EntMan;

        EntityUid humanUid = default;
        EntityUid vendorUid = default;

        await server.WaitPost(() =>
        {
            humanUid = entMan.SpawnAtPosition(Human, testMap.GridCoords);
            vendorUid = entMan.SpawnAtPosition(_reversedVendor, testMap.GridCoords);
        });

        await Pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            entMan.EventBus.RaiseLocalEvent(vendorUid, new CMVendorVendBuiMsg(0, 0, new List<int>())
            {
                Actor = humanUid,
                UiKey = CMAutomatedVendorUI.Key,
            });
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() => AssertDressed(entMan, humanUid, new HashSet<EntityUid>(), "a kit listed back to front"));
    }

    // Content test: the real scenario vendors and their kits.
    [TestCase("ColMarTechScenarioNanoplantNt")]
    [TestCase("ColMarTechScenarioNanoplantSyndie")]
    public async Task BaseGearAndLoadoutAreWornNotDropped(string vendorId)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var entMan = server.EntMan;

        EntityUid humanUid = default;
        EntityUid vendorUid = default;

        await server.WaitPost(() =>
        {
            humanUid = entMan.SpawnAtPosition(Human, testMap.GridCoords);
            vendorUid = entMan.SpawnAtPosition(vendorId, testMap.GridCoords);
        });

        await Pair.RunTicksSync(2);

        // Base gear first, then the first loadout, which replaces some of the base gear.
        foreach (var section in new[] { 0, 1 })
        {
            var wornBefore = new HashSet<EntityUid>();

            await server.WaitPost(() =>
            {
                var slots = entMan.System<InventorySystem>().GetSlotEnumerator(humanUid);
                while (slots.MoveNext(out var slot))
                {
                    if (slot.ContainedEntity is { } wornUid)
                        wornBefore.Add(wornUid);
                }

                entMan.EventBus.RaiseLocalEvent(vendorUid, new CMVendorVendBuiMsg(section, 0, new List<int>())
                {
                    Actor = humanUid,
                    UiKey = CMAutomatedVendorUI.Key,
                });
            });

            await Pair.RunTicksSync(2);

            await server.WaitAssertion(() => AssertDressed(entMan, humanUid, wornBefore, $"{vendorId}, after section {section}"));
        }
    }

    private static void AssertDressed(IEntityManager entMan, EntityUid humanUid, HashSet<EntityUid> wornBefore, string context)
    {
        var inventorySystem = entMan.System<InventorySystem>();
        var handsSystem = entMan.System<SharedHandsSystem>();
        var containerSystem = entMan.System<SharedContainerSystem>();

        foreach (var slot in new[] { "jumpsuit", "back", "belt", "id", "shoes" })
        {
            Assert.That(inventorySystem.TryGetSlotEntity(humanUid, slot, out _), Is.True,
                $"{context}: nothing was worn in the {slot} slot");
        }

        inventorySystem.TryGetSlotEntity(humanUid, "id", out var idUid);
        Assert.That(entMan.HasComponent<PdaComponent>(idUid), Is.True,
            $"{context}: the PDA should be worn as the ID, whatever was listed before it");

        foreach (var heldUid in handsSystem.EnumerateHeld(humanUid))
        {
            Assert.That(entMan.TryGetComponent<ClothingComponent>(heldUid, out var clothingComponent) &&
                        HasFreeSlotFor(inventorySystem, humanUid, clothingComponent),
                Is.False,
                $"{context}: {entMan.ToPrettyString(heldUid)} is held although a slot for it is free");
        }

        // Everything vended is worn, held or packed away, never left lying about.
        var vendedQuery = entMan.AllEntityQueryEnumerator<RMCRecentlyVendedComponent>();
        while (vendedQuery.MoveNext(out var vendedUid, out _))
        {
            // A worn item that the loadout replaced is taken off on purpose.
            if (containerSystem.IsEntityInContainer(vendedUid) || wornBefore.Contains(vendedUid))
                continue;

            Assert.Fail($"{context}: {entMan.ToPrettyString(vendedUid)} was left on the floor");
        }
    }

    private static bool HasFreeSlotFor(InventorySystem inventorySystem, EntityUid humanUid, ClothingComponent clothingComponent)
    {
        var slots = inventorySystem.GetSlotEnumerator(humanUid, clothingComponent.Slots);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity == null)
                return true;
        }

        return false;
    }
}
