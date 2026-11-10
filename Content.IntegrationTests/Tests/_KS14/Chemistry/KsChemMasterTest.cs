#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Labels.Components;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Content.Shared.Chemistry.Reagent;
using NUnit.Framework.Interfaces;

namespace Content.IntegrationTests.Tests._KS14.Chemistry;

/// <summary>
/// untrusted pill requests must be bounded and preserve normal production.
/// </summary>
public sealed class KsChemMasterTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    public override async Task DoTeardown()
    {
        // The pool's dirty-disposal warning can replace an assertion failure with a skipped result.
        var result = TestContext.CurrentContext.Result;
        var failure = result.Outcome.Status == TestStatus.Failed ? result.Message : null;
        await base.DoTeardown();
        if (failure != null)
            Assert.Fail(failure);
    }

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: KsTestChemMasterHighDose
          parent: ChemMaster
          components:
          - type: ChemMaster
            pillDosageLimit: 4294967295
        """;

    private static readonly EntProtoId MachinePrototype = "ChemMaster";
    private static readonly EntProtoId CanisterPrototype = "PillCanister";
    private static readonly EntProtoId BeakerPrototype = "LargeBeaker";
    private static readonly EntProtoId BottlePrototype = "ChemistryEmptyBottle01";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> SugarReagent = "Sugar";
    private readonly EntProtoId _highDosePrototype = "KsTestChemMasterHighDose";

    private IEntityManager _entities = default!;
    private SharedSolutionContainerSystem _solutions = default!;
    private EntityUid _machineUid;
    private EntityUid _actorUid;
    private EntityUid _canisterUid;
    private StorageComponent _storageComponent = default!;
    private Solution _source = default!;
    private readonly List<EntityUid> _created = new();
    private HashSet<EntityUid> _audioBefore = default!;
    private Action? _onPillCreated;

    [TestCase(false)]
    [TestCase(true)]
    public async Task RejectsOverCapacity(bool external)
    {
        await WithMachine(external, () =>
        {
            Create(2, 11);
            AssertUnchanged();
        });
    }

    [TestCase(2u, 2147483648u)] // The original uint multiplication wrapped to zero.
    [TestCase(1u, uint.MaxValue)]
    [TestCase(20u, 214748365u)] // The original multiplication wrapped to a small positive volume.
    [TestCase(2u, 0u)]
    [TestCase(0u, 1u)]
    [TestCase(21u, 1u)]
    public async Task RejectsInvalidRequest(uint dosage, uint number)
    {
        await WithMachine(false, () =>
        {
            Create(dosage, number);
            AssertUnchanged();
        });
    }

    [TestCase(21474837u, 1u)] // Fits uint, but cannot be represented by FixedPoint2.
    [TestCase(2147483648u, 2u)] // A bounded count must not allow the volume calculation to wrap.
    [TestCase(uint.MaxValue, 2u)]
    public async Task RejectsUnrepresentableVolume(uint dosage, uint number)
    {
        await WithMachine(false, () =>
        {
            Create(dosage, number);
            AssertUnchanged();
        }, highDose: true);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NormalBatchPreservesDoseMixtureTypeAndLabel(bool external)
    {
        await WithMachine(external, () =>
        {
            const string label = "Лекарство 20u";
            _entities.EventBus.RaiseLocalEvent(_machineUid, new ChemMasterSetPillTypeMessage(4)
            {
                Actor = _actorUid,
                UiKey = ChemMasterUiKey.Key,
            });
            Create(20, 3, label);

            Assert.That(_storageComponent.Container.ContainedEntities.Count, Is.EqualTo(3));
            Assert.That(_source.Volume, Is.EqualTo((FixedPoint2) 40));
            Assert.That(_source.GetTotalPrototypeQuantity(WaterReagent), Is.EqualTo((FixedPoint2) 20));
            Assert.That(_source.GetTotalPrototypeQuantity(SugarReagent), Is.EqualTo((FixedPoint2) 20));
            Assert.That(Label(_canisterUid), Is.EqualTo(label));
            Assert.That(NewInsertionSounds().Count(), Is.EqualTo(3), "Successful pills retain their storage sounds.");
            foreach (var sound in NewInsertionSounds())
            {
                Assert.That(sound.Params.Volume, Is.EqualTo(-2f));
                Assert.That(sound.Params.MaxDistance, Is.EqualTo(7f));
            }
            foreach (var pill in _storageComponent.Container.ContainedEntities)
            {
                Assert.That(_entities.GetComponent<PillComponent>(pill).PillType, Is.EqualTo(4));
                Assert.That(Label(pill), Is.EqualTo(label));
                Assert.That(_solutions.TryGetSolution(pill, SharedChemMaster.PillSolutionName, out _, out var solution), Is.True);
                Assert.That(solution!.MaxVolume, Is.EqualTo((FixedPoint2) 20));
                Assert.That(solution.Volume, Is.EqualTo((FixedPoint2) 20));
                Assert.That(solution.GetTotalPrototypeQuantity(WaterReagent), Is.EqualTo((FixedPoint2) 10));
                Assert.That(solution.GetTotalPrototypeQuantity(SugarReagent), Is.EqualTo((FixedPoint2) 10));
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PartialCanisterAcceptsExactRemainderAndRejectsOverflow(bool external)
    {
        await WithMachine(external, () =>
        {
            Create(10, 3);
            Create(1, 8, "must not replace label");
            Assert.That(_storageComponent.Container.ContainedEntities.Count, Is.EqualTo(3));
            Assert.That(_source.Volume, Is.EqualTo((FixedPoint2) 70));
            Assert.That(Label(_canisterUid), Is.EqualTo("medicine"));

            Create(10, 7);
            Assert.That(_storageComponent.Container.ContainedEntities.Count, Is.EqualTo(10));
            Assert.That(_source.Volume, Is.EqualTo(FixedPoint2.Zero));
            Create(1, 1, "full");
            Assert.That(_created.Count, Is.EqualTo(10));
            Assert.That(Label(_canisterUid), Is.EqualTo("medicine"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InsufficientReagentsLeaveNoPillsOrChangedLabel(bool external)
    {
        await WithMachine(external, () =>
        {
            Create(20, 6);
            AssertUnchanged();
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EmptySourceDoesNotSpawnPills(bool external)
    {
        await WithMachine(external, () =>
        {
            _source.RemoveAllSolution();
            Create(20, 10);
            AssertUnchanged(expectedVolume: 0);
        });
    }

    [Test]
    public async Task MissingExternalSourceDoesNotSpawnPills()
    {
        await WithMachine(true, () =>
        {
            var beakerUid = Server.System<ItemSlotsSystem>().GetItemOrNull(_machineUid, SharedChemMaster.InputSlotName);
            Assert.That(beakerUid, Is.Not.Null);
            _entities.DeleteEntity(beakerUid!.Value);
            Create(20, 10);
            AssertUnchanged();
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SourceChangeDuringPreparationRollsBackBatch(bool external)
    {
        await WithMachine(external, () =>
        {
            _onPillCreated = () => _source.RemoveAllSolution();
            Create(10, 3);
            Assert.That(_created.Count, Is.EqualTo(3));
            AssertUnchanged(requireNoSpawns: false, expectedVolume: 0);
        });
    }

    [Test]
    public async Task RejectsLongLabel()
    {
        await WithMachine(false, () =>
        {
            Create(10, 1, new string('x', (int) SharedChemMaster.LabelMaxLength + 1));
            AssertUnchanged();
        });
    }

    [Test]
    public async Task RejectsNullLabel()
    {
        await WithMachine(false, () =>
        {
            Create(10, 1, null!);
            AssertUnchanged();
        });
    }

    [Test]
    public async Task SilentStorageUserKeepsSilentInsertion()
    {
        await WithMachine(false, () =>
        {
            Server.System<TagSystem>().AddTag(_actorUid, _storageComponent.SilentStorageUserTag);
            Create(10, 3);
            Assert.That(_storageComponent.Container.ContainedEntities.Count, Is.EqualTo(3));
            Assert.That(NewInsertionSounds(), Is.Empty);
        });
    }

    [TestCase(0u, false)]
    [TestCase(19u, true)]
    public async Task AcceptsBoundaryPillTypesAndLabels(uint pillType, bool longestLabel)
    {
        await WithMachine(false, () =>
        {
            var label = longestLabel ? new string('x', (int) SharedChemMaster.LabelMaxLength) : "";
            _entities.EventBus.RaiseLocalEvent(_machineUid, new ChemMasterSetPillTypeMessage(pillType)
            {
                Actor = _actorUid,
                UiKey = ChemMasterUiKey.Key,
            });
            Create(1, 1, label);
            var pill = _storageComponent.Container.ContainedEntities.Single();
            Assert.That(_entities.GetComponent<PillComponent>(pill).PillType, Is.EqualTo(pillType));
            Assert.That(Label(pill) ?? "", Is.EqualTo(label));
            Assert.That(Label(_canisterUid) ?? "", Is.EqualTo(label));
            Assert.That(_source.Volume, Is.EqualTo((FixedPoint2) 99));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BottleOutputStillWorks(bool external)
    {
        await WithMachine(external, () =>
        {
            _entities.DeleteEntity(_canisterUid);
            var bottle = _entities.SpawnEntity(BottlePrototype,
                _entities.GetComponent<TransformComponent>(_machineUid).Coordinates);
            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(_machineUid,
                SharedChemMaster.OutputSlotName, bottle, user: null), Is.True);
            _entities.EventBus.RaiseLocalEvent(_machineUid, new ChemMasterOutputToBottleMessage(30, "bottle")
            {
                Actor = _actorUid,
                UiKey = ChemMasterUiKey.Key,
            });
            Assert.That(_solutions.TryGetSolution(bottle, SharedChemMaster.BottleSolutionName, out _, out var solution), Is.True);
            Assert.That(solution!.Volume, Is.EqualTo((FixedPoint2) 30));
            Assert.That(_source.Volume, Is.EqualTo((FixedPoint2) 70));
            Assert.That(Label(bottle), Is.EqualTo("bottle"));
            Assert.That(_created, Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedInsertionRollsBackWholeBatch(bool external)
    {
        await WithMachine(external, () =>
        {
            // First pill fits; the second is vetoed despite free grid area.
            _onPillCreated = () =>
            {
                if (_created.Count == 2)
                    _storageComponent.Blacklist = new EntityWhitelist { Components = ["Pill"] };
            };
            Create(10, 3);
            Assert.That(_created.Count, Is.EqualTo(2), "Stop on the first failed insertion.");
            AssertUnchanged(requireNoSpawns: false);
        });
    }

    private void Create(uint dosage, uint number, string label = "medicine")
    {
        _entities.EventBus.RaiseLocalEvent(_machineUid, new ChemMasterCreatePillsMessage(dosage, number, label)
        {
            Actor = _actorUid,
            UiKey = ChemMasterUiKey.Key,
        });
    }

    private string? Label(EntityUid uid) => _entities.GetComponent<LabelComponent>(uid).CurrentLabel;

    private IEnumerable<AudioComponent> NewInsertionSounds()
    {
        var path = ((SoundPathSpecifier) _storageComponent.StorageInsertSound!).Path.ToString();
        return _entities.EntityQuery<AudioComponent>()
            .Where(audio => !_audioBefore.Contains(audio.Owner) && audio.FileName == path);
    }

    private void AssertUnchanged(bool requireNoSpawns = true, int expectedVolume = 100)
    {
        Assert.That(_storageComponent.Container.ContainedEntities, Is.Empty);
        Assert.That(_source.Volume, Is.EqualTo((FixedPoint2) expectedVolume));
        Assert.That(Label(_canisterUid), Is.EqualTo("original"));
        Assert.That(NewInsertionSounds(), Is.Empty, "Rejected batches must not play successful insertion sounds.");
        Assert.That(_created.Where(_entities.EntityExists), Is.Empty, "No loose pills may survive rejection.");
        if (requireNoSpawns)
            Assert.That(_created, Is.Empty, "Invalid requests must be rejected before spawning entities.");
    }

    private async Task WithMachine(bool external, Action assertions, bool highDose = false)
    {
        var map = await Pair.CreateTestMap();
        _entities = Server.ResolveDependency<IEntityManager>();
        _solutions = Server.System<SharedSolutionContainerSystem>();
        await Server.WaitAssertion(() =>
        {
            _machineUid = _entities.SpawnEntity(highDose ? _highDosePrototype : MachinePrototype, map.GridCoords);
            _actorUid = _entities.SpawnEntity(null, map.GridCoords);
            _canisterUid = _entities.SpawnEntity(CanisterPrototype, map.GridCoords);
            var beakerUid = _entities.SpawnEntity(BeakerPrototype, map.GridCoords);
            var itemSlotsSystem = Server.System<ItemSlotsSystem>();
            Assert.That(itemSlotsSystem.TryInsert(_machineUid, SharedChemMaster.OutputSlotName, _canisterUid, user: null), Is.True);
            Assert.That(itemSlotsSystem.TryInsert(_machineUid, SharedChemMaster.InputSlotName, beakerUid, user: null), Is.True);
            _storageComponent = _entities.GetComponent<StorageComponent>(_canisterUid);
            Server.System<LabelSystem>().Label(_canisterUid, "original");
            _entities.EventBus.RaiseLocalEvent(_machineUid, new ChemMasterOutputDrawSourceMessage(
                external ? ChemMasterDrawSource.External : ChemMasterDrawSource.Internal)
            {
                Actor = _actorUid,
                UiKey = ChemMasterUiKey.Key,
            });
            Assert.That(_solutions.TryGetSolution(external ? beakerUid : _machineUid,
                external ? "beaker" : SharedChemMaster.BufferSolutionName, out var source, out var solution), Is.True);
            _source = solution!;
            Assert.That(_solutions.TryAddReagent(source!.Value, WaterReagent, 50), Is.True);
            Assert.That(_solutions.TryAddReagent(source.Value, SugarReagent, 50), Is.True);
            _audioBefore = _entities.EntityQuery<AudioComponent>().Select(audio => audio.Owner).ToHashSet();

            void OnInitialized(Entity<MetaDataComponent> entity)
            {
                if (entity.Comp.EntityPrototype?.ID != "Pill")
                    return;
                _created.Add(entity.Owner);
                // A future regression must fail promptly instead of exhausting the test runner's memory.
                Assert.That(_created.Count, Is.LessThanOrEqualTo(20), "Unbounded pill creation detected.");
                _onPillCreated?.Invoke();
            }

            _entities.EntityInitialized += OnInitialized;
            try
            {
                assertions();
            }
            finally
            {
                _entities.EntityInitialized -= OnInitialized;
                foreach (var pill in _created.Where(_entities.EntityExists))
                    _entities.DeleteEntity(pill);
                _entities.DeleteEntity(_machineUid);
                _entities.DeleteEntity(_actorUid);
                if (_entities.EntityExists(_canisterUid))
                    _entities.DeleteEntity(_canisterUid);
                if (_entities.EntityExists(beakerUid))
                    _entities.DeleteEntity(beakerUid);
            }
        });
    }
}
