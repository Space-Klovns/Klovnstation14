#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._RMC14.Vendors;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     The field commander defenders' vendor: what it sells, and the base gear that comes with a first loadout.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedCMAutomatedVendorSystem))]
public sealed class KsFieldCommanderVendorTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";

    private static readonly EntProtoId FieldCommanderVendor = "KsColMarTechFieldCommanderNt";
    private static readonly EntProtoId FieldEngineerRcd = "KsRCDFieldEngineer";
    private static readonly EntProtoId BruteAutoInjector = "BruteAutoInjector";
    private static readonly EntProtoId TricordrazineHypospray = "KsHyposprayTricordrazine";
    private static readonly ProtoId<ReagentPrototype> Tricordrazine = "Tricordrazine";

    // Exists only in the test prototypes below.
    private readonly EntProtoId _alsoVendVendor = "KsTestVendorAlsoVend";
    private readonly EntProtoId _baseItem = "KsTestVendorBaseItem";
    private readonly EntProtoId _firstLoadoutItem = "KsTestVendorFirstLoadoutItem";
    private readonly EntProtoId _secondLoadoutItem = "KsTestVendorSecondLoadoutItem";

    // Base gear in section 0 and two loadouts in section 1, of which a player may take two, so that a second loadout
    //      in one life can be asked for. Every bundle is one item that tells the others apart.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestVendorBaseItem
  parent: BaseItem
  categories: [ HideSpawnMenu ]

- type: entity
  id: KsTestVendorFirstLoadoutItem
  parent: BaseItem
  categories: [ HideSpawnMenu ]

- type: entity
  id: KsTestVendorSecondLoadoutItem
  parent: BaseItem
  categories: [ HideSpawnMenu ]

- type: entity
  id: KsTestVendorBaseBundle
  categories: [ HideSpawnMenu ]
  components:
  - type: CMVendorBundle
    bundle:
    - KsTestVendorBaseItem

- type: entity
  parent: ColMarTechScenarioBase
  id: KsTestVendorAlsoVend
  components:
  - type: CMAutomatedVendor
    sections:
    - name: Base
      choices: { BaseGear: 1 }
      entries:
      - id: KsTestVendorBaseBundle
    - name: Loadout
      choices: { CMClass: 2 }
      ksAlsoVend: KsTestVendorBaseBundle
      entries:
      - id: KsTestVendorFirstLoadoutItem
      - id: KsTestVendorSecondLoadoutItem
";

    [Test]
    public async Task EngineerLoadoutHasTheFieldEngineerRcd()
    {
        var protoMan = Server.ProtoMan;
        var factory = Server.EntMan.ComponentFactory;

        await Server.WaitAssertion(() =>
        {
            var vendor = GetVendor(protoMan, factory);
            var carriesRcd = false;
            foreach (var entry in vendor.Sections.SelectMany(section => section.Entries))
            {
                if (!protoMan.Index(entry.Id).TryGetComponent<CMVendorBundleComponent>(out var bundle, factory))
                    continue;

                // The component only lets its system call into its list, but anyone may read it.
                IReadOnlyList<EntProtoId> items = bundle.Bundle;
                if (items.Contains(FieldEngineerRcd))
                    carriesRcd = true;
            }

            Assert.That(carriesRcd, Is.True, "a loadout of the field commander vendor should hold the field engineer RCD");
        });
    }

    [Test]
    public async Task VendorSellsTricordrazineHyposprayAndBruteInjector()
    {
        var protoMan = Server.ProtoMan;
        var factory = Server.EntMan.ComponentFactory;

        await Server.WaitAssertion(() =>
        {
            var vendor = GetVendor(protoMan, factory);
            var sold = vendor.Sections.SelectMany(section => section.Entries).Select(entry => entry.Id).ToHashSet();

            Assert.That(sold, Does.Contain(TricordrazineHypospray), "the vendor should sell the tricordrazine hypospray");
            Assert.That(sold, Does.Contain(BruteAutoInjector), "the vendor should sell the brute auto-injector");
        });
    }

    [Test]
    public async Task TricordrazineHyposprayComesFullOfTricordrazine()
    {
        var testMap = await Pair.CreateTestMap();
        var entMan = Server.EntMan;
        var solutionSystem = entMan.System<SharedSolutionContainerSystem>();

        await Server.WaitAssertion(() =>
        {
            var hypospray = entMan.SpawnAtPosition(TricordrazineHypospray, testMap.GridCoords);
            Assert.That(solutionSystem.TryGetSolution(hypospray, "hypospray", out _, out var solution), Is.True);
            Assert.That(solution!.Volume, Is.GreaterThan(FixedPoint2.Zero), "the hypospray should come loaded");
            Assert.That(solution.GetReagentQuantity(new ReagentId(Tricordrazine, null)), Is.EqualTo(solution.MaxVolume),
                "the hypospray should come full, and of tricordrazine only");
        });
    }

    [Test]
    public async Task TheMapUsesTheFieldCommanderVendor()
    {
        var found = 0;
        await Server.WaitAssertion(() =>
        {
            var resourceManager = Server.ResolveDependency<IResourceManager>();
            using var reader = resourceManager.ContentFileReadText(new ResPath("/Maps/_KS14/Scenarios/field_commander.yml"));
            while (reader.ReadLine() is { } line)
            {
                if (!line.StartsWith("- proto: ") || !line.Contains("ColMarTech"))
                    continue;

                Assert.That(line.Trim(), Is.EqualTo($"- proto: {FieldCommanderVendor.Id}"), "the map holds another scenario vendor");
                found++;
            }
        });

        Assert.That(found, Is.GreaterThan(0), "the map should place the field commander vendor");
    }

    [Test]
    public async Task ALoadoutBringsBaseGearOncePerLife()
    {
        var (humanUid, vendorUid) = await SpawnHumanAndVendor();

        await Vend(vendorUid, humanUid, section: 1, entry: 0);
        await AssertCounts(baseGear: 1, first: 1, second: 0);

        // A second loadout in the same life brings no second set.
        await Vend(vendorUid, humanUid, section: 1, entry: 1);
        await AssertCounts(baseGear: 1, first: 1, second: 1);

        // And the base gear's own section is spent, however it is asked for.
        await Vend(vendorUid, humanUid, section: 0, entry: 0);
        await AssertCounts(baseGear: 1, first: 1, second: 1);
    }

    [Test]
    public async Task BaseGearTakenFirstIsNotRepeatedByALoadout()
    {
        var (humanUid, vendorUid) = await SpawnHumanAndVendor();

        await Vend(vendorUid, humanUid, section: 0, entry: 0);
        await AssertCounts(baseGear: 1, first: 0, second: 0);

        await Vend(vendorUid, humanUid, section: 1, entry: 0);
        await AssertCounts(baseGear: 1, first: 1, second: 0);
    }

    [Test]
    public async Task BaseGearAloneStillWorks()
    {
        var (humanUid, vendorUid) = await SpawnHumanAndVendor();

        await Vend(vendorUid, humanUid, section: 0, entry: 0);
        await AssertCounts(baseGear: 1, first: 0, second: 0);

        await Vend(vendorUid, humanUid, section: 0, entry: 0);
        await AssertCounts(baseGear: 1, first: 0, second: 0);
    }

    [Test]
    public async Task AnotherPlayerGetsTheirOwnBaseGear()
    {
        var (humanUid, vendorUid) = await SpawnHumanAndVendor();
        var entMan = Server.EntMan;
        EntityUid otherUid = default;
        await Server.WaitPost(() => otherUid = entMan.SpawnAtPosition(Human, entMan.GetComponent<TransformComponent>(humanUid).Coordinates));

        await Vend(vendorUid, humanUid, section: 1, entry: 0);
        await Vend(vendorUid, otherUid, section: 1, entry: 0);

        await AssertCounts(baseGear: 2, first: 2, second: 0);
    }

    [Test]
    public async Task TheVendorSaysWhetherALoadoutBringsBaseGear()
    {
        var (humanUid, vendorUid) = await SpawnHumanAndVendor();
        var entMan = Server.EntMan;
        var system = entMan.System<SharedCMAutomatedVendorSystem>();

        bool Pending()
        {
            var vendor = entMan.GetComponent<CMAutomatedVendorComponent>(vendorUid);
            entMan.TryGetComponent<CMVendorUserComponent>(humanUid, out var user);
            return system.KsAlsoVendPending(vendor, vendor.Sections[1], user);
        }

        await Server.WaitAssertion(() => Assert.That(Pending(), Is.True));
        await Vend(vendorUid, humanUid, section: 1, entry: 0);
        await Server.WaitAssertion(() => Assert.That(Pending(), Is.False));
    }

    private async Task<(EntityUid Human, EntityUid Vendor)> SpawnHumanAndVendor()
    {
        var testMap = await Pair.CreateTestMap();
        var entMan = Server.EntMan;
        EntityUid humanUid = default;
        EntityUid vendorUid = default;

        await Server.WaitPost(() =>
        {
            humanUid = entMan.SpawnAtPosition(Human, testMap.GridCoords);
            vendorUid = entMan.SpawnAtPosition(_alsoVendVendor, testMap.GridCoords);
        });

        await Pair.RunTicksSync(2);
        return (humanUid, vendorUid);
    }

    private async Task Vend(EntityUid vendorUid, EntityUid humanUid, int section, int entry)
    {
        await Server.WaitPost(() =>
        {
            Server.EntMan.EventBus.RaiseLocalEvent(vendorUid, new CMVendorVendBuiMsg(section, entry, new List<int>())
            {
                Actor = humanUid,
                UiKey = CMAutomatedVendorUI.Key,
            });
        });

        await Pair.RunTicksSync(2);
    }

    private async Task AssertCounts(int baseGear, int first, int second)
    {
        var entMan = Server.EntMan;
        var counts = new Dictionary<string, int>
        {
            [_baseItem] = 0,
            [_firstLoadoutItem] = 0,
            [_secondLoadoutItem] = 0,
        };

        await Server.WaitAssertion(() =>
        {
            var query = entMan.AllEntityQueryEnumerator<RMCRecentlyVendedComponent, MetaDataComponent>();
            while (query.MoveNext(out _, out _, out var metaData))
            {
                if (metaData.EntityPrototype is { } prototype && counts.ContainsKey(prototype.ID))
                    counts[prototype.ID]++;
            }

            Assert.That(counts[_baseItem], Is.EqualTo(baseGear), "base gear vended");
            Assert.That(counts[_firstLoadoutItem], Is.EqualTo(first), "first loadout vended");
            Assert.That(counts[_secondLoadoutItem], Is.EqualTo(second), "second loadout vended");
        });
    }

    private static CMAutomatedVendorComponent GetVendor(IPrototypeManager protoMan, IComponentFactory factory)
    {
        Assert.That(protoMan.Index(FieldCommanderVendor).TryGetComponent<CMAutomatedVendorComponent>(out var vendor, factory), Is.True);
        return vendor!;
    }
}
