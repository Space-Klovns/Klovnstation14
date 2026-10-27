#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Materials;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Materials;

/// <summary>
///     A material storage's limit is shared by every material in it: a stack made of two materials fills it up to the
///         limit, not up to the limit once per material.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedMaterialStorageSystem))]
public sealed class KsMaterialStorageLimitTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const int StorageLimit = 500;

    private readonly EntProtoId _storage = "KsTestMaterialStorage";
    private readonly EntProtoId _dualSheet = "KsTestDualMaterialSheet";

    [TestPrototypes]
    private const string Prototypes = @"
- type: stack
  id: KsTestDualMaterial
  name: stack-steel # any existing name: the test is about the materials, not the stack
  spawn: KsTestDualMaterialSheet
  maxCount: 30

- type: entity
  id: KsTestDualMaterialSheet
  components:
  - type: Material
  - type: PhysicalComposition
    materialComposition:
      Steel: 100
      Glass: 100
  - type: Stack
    stackType: KsTestDualMaterial
    count: 3

- type: entity
  id: KsTestMaterialStorage
  components:
  - type: MaterialStorage
    storageLimit: 500
";

    [Test]
    public async Task AStackOfTwoMaterialsFillsTheStorageOnlyToItsLimit()
    {
        await Server.WaitAssertion(() =>
        {
            var storageSystem = SEntMan.System<SharedMaterialStorageSystem>();
            var storage = SEntMan.SpawnEntity(_storage, MapCoordinates.Nullspace);
            var sheets = SEntMan.SpawnEntity(_dualSheet, MapCoordinates.Nullspace);

            // 3 sheets of 100 steel and 100 glass each: 600 in all, against a limit of 500
            Assert.That(storageSystem.TryInsertMaterialEntity(storage, sheets, storage), Is.True, "precondition: it goes in");

            Assert.That(storageSystem.GetTotalMaterialAmount(storage), Is.EqualTo(StorageLimit), "filled to the limit, and no further");
            Assert.That(storageSystem.GetMaterialAmount(storage, "Steel"), Is.EqualTo(300));
            Assert.That(storageSystem.GetMaterialAmount(storage, "Glass"), Is.EqualTo(StorageLimit - 300), "the second material gets what the first left");
        });
    }
}
