#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._ES.Weapons.Ranged.Attachments;
using Content.Shared._ES.Weapons.Ranged.Attachments.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Battlefield14.Weapons;

/// <summary>
///     ES gun attachments as Battlefield14 uses them: a suppressor fits only its own slot and swaps the gunshot sound
///         while it is on.
/// </summary>
[TestFixture]
[TestOf(typeof(ESSharedGunAttachmentsSystem))]
public sealed class BF14GunAttachmentTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: Tag
  id: BF14TestSuppressor

- type: entity
  parent: WeaponPistolMk58
  id: BF14TestAttachablePistol
  components:
  - type: ESAttachableGun
    slots:
    - name: es-gun-attachments-slot-name-barrel
      containerId: attachment_barrel
      whitelist:
        tags:
        - BF14TestSuppressor

- type: entity
  id: BF14TestSuppressor
  components:
  - type: Item
  - type: Tag
    tags:
    - BF14TestSuppressor
  - type: ESGunAttachment
  - type: ESGunSoundAttachment
    sound: /Audio/Weapons/Guns/Gunshots/silenced.ogg

- type: entity
  id: BF14TestWrongAttachment
  components:
  - type: Item
  - type: ESGunAttachment
";

    private readonly EntProtoId _pistol = "BF14TestAttachablePistol";
    private readonly EntProtoId _suppressor = "BF14TestSuppressor";
    private readonly EntProtoId _wrongAttachment = "BF14TestWrongAttachment";
    private static readonly ResPath SuppressedSound = new("/Audio/Weapons/Guns/Gunshots/silenced.ogg");

    [Test]
    public async Task SuppressorSwapsTheGunshotWhileFitted()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var attachmentsSystem = server.System<ESSharedGunAttachmentsSystem>();
        var containerSystem = server.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var origin = new EntityCoordinates(testMap.MapUid, 0, 0);
            var pistolUid = SSpawnAtPosition(_pistol, origin);
            var gunComponent = SEntMan.GetComponent<GunComponent>(pistolUid);
            var attachableComponent = SEntMan.GetComponent<ESAttachableGunComponent>(pistolUid);
            var slot = attachableComponent.Slots[0];
            var originalSound = gunComponent.SoundGunshotModified;

            Assert.That(attachmentsSystem.TryInsertAttachment((pistolUid, attachableComponent), SSpawnAtPosition(_wrongAttachment, origin), slot),
                Is.False,
                "an attachment the slot does not whitelist went on");

            var suppressorUid = SSpawnAtPosition(_suppressor, origin);
            Assert.That(attachmentsSystem.TryInsertAttachment((pistolUid, attachableComponent), suppressorUid, slot), Is.True,
                "the suppressor did not go on");
            Assert.That(gunComponent.SoundGunshotModified, Is.TypeOf<SoundPathSpecifier>()
                    .And.Property(nameof(SoundPathSpecifier.Path)).EqualTo(SuppressedSound),
                "the suppressor did not change the gunshot");

            containerSystem.Remove(suppressorUid, containerSystem.GetContainer(pistolUid, slot.ContainerId));
            Assert.That(gunComponent.SoundGunshotModified, Is.EqualTo(originalSound),
                "the gunshot stayed suppressed after the suppressor came off");
        });
    }
}
