#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._KS14.GhostRoleNotice;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using NUnit.Framework;
using Robust.Shared.Audio.Components;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using ClientNoticeSystem = Content.Client._KS14.GhostRoleNotice.KsGhostRoleNoticeSystem;

namespace Content.IntegrationTests.Tests._KS14.GhostRoleNotice;

/// <summary>
///     Shared helpers for the ghost role notice fixtures.
/// </summary>
public abstract class KsGhostRoleNoticeTestBase : GameTest
{
    protected const string NoticeSoundPath = "/Audio/_KS14/GhostRoleNotice/ghost_role_available.ogg";

    protected readonly EntProtoId NotableRole = "KsGhostRoleNoticeTestNotable";
    protected readonly EntProtoId OtherNotableRole = "KsGhostRoleNoticeTestNotableOther";
    protected readonly EntProtoId UnmarkedAntagRole = "KsGhostRoleNoticeTestUnmarkedAntag";
    protected readonly EntProtoId NotableHumanoidSpawner = "KsGhostRoleNoticeTestHumanoidSpawner";
    protected readonly EntProtoId LivingMob = "KsGhostRoleNoticeTestMob";
    protected readonly EntProtoId Ghost = "MobObserver";

    [TestPrototypes]
    protected const string Prototypes = @"
- type: entity
  id: KsGhostRoleNoticeTestNotable
  components:
  - type: MindContainer
  - type: GhostTakeoverAvailable
  - type: KsGhostRoleNotable
  - type: GhostRole
    name: ks-test-role
    description: ks-test-role

- type: entity
  id: KsGhostRoleNoticeTestNotableOther
  components:
  - type: KsGhostRoleNotable
  - type: GhostRole
    name: ks-test-other-role
    description: ks-test-role

- type: entity
  id: KsGhostRoleNoticeTestUnmarkedAntag
  components:
  - type: GhostRole
    name: ks-test-role
    description: ks-test-role
    mindRoles:
    - MindRoleGhostRoleSoloAntagonist

- type: randomHumanoidSettings
  id: KsGhostRoleNoticeTestHumanoid
  components:
  - type: GhostRole
    name: ks-test-role
    description: ks-test-role
  - type: KsGhostRoleNotable

- type: entity
  id: KsGhostRoleNoticeTestHumanoidSpawner
  components:
  - type: RandomHumanoidSpawner
    settings: KsGhostRoleNoticeTestHumanoid

- type: entity
  id: KsGhostRoleNoticeTestMob
  components:
  - type: MindContainer
";

    protected ClientNoticeSystem ClientNotice => CEntMan.System<ClientNoticeSystem>();

    protected int ServerNoticeSounds => SEntMan.AllComponents<AudioComponent>().Count(pair => pair.Component.FileName == NoticeSoundPath);

    /// <summary>
    ///     Puts the test player into a fresh mob of <paramref name="proto"/>.
    /// </summary>
    protected async Task PossessAsync(EntProtoId proto)
    {
        await Server.WaitPost(() => PutPlayerInto(SEntMan.SpawnEntity(proto, MapCoordinates.Nullspace)));
        await Pair.RunTicksSync(10);
    }

    /// <summary>
    ///     Spawns every one of <paramref name="protos"/> in the same tick.
    /// </summary>
    protected async Task SpawnRolesAsync(params EntProtoId[] protos)
    {
        await Server.WaitPost(() =>
        {
            foreach (var proto in protos)
                SEntMan.SpawnEntity(proto, MapCoordinates.Nullspace);
        });
        await Pair.RunTicksSync(10);
    }

    protected void PutPlayerInto(EntityUid mobUid)
    {
        var mindSystem = SEntMan.System<SharedMindSystem>();
        mindSystem.TransferTo(mindSystem.CreateMind(ServerSession!.UserId), mobUid);
    }
}

/// <summary>
///     Notable ghost roles that appear during a round are announced to ghosts, and only to ghosts.
/// </summary>
[TestFixture]
public sealed class KsGhostRoleNoticeInRoundTests : KsGhostRoleNoticeTestBase
{
    public override PoolSettings PoolSettings => new() { Connected = true, DummyTicker = false, Dirty = true };

    [Test]
    public async Task NotableRoleReachesGhostsAndNotTheLiving()
    {
        await PossessAsync(Ghost);
        var soundsBefore = ServerNoticeSounds;
        var noticesBefore = ClientNotice.ReceivedCount;

        await SpawnRolesAsync(NotableRole);

        await Server.WaitAssertion(() => Assert.That(ServerNoticeSounds, Is.GreaterThan(soundsBefore), "the sound is played for the ghost"));
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore + 1), "and so is the chat line"));

        await PossessAsync(LivingMob);
        soundsBefore = ServerNoticeSounds;
        noticesBefore = ClientNotice.ReceivedCount;

        await SpawnRolesAsync(NotableRole);

        await Server.WaitAssertion(() => Assert.That(ServerNoticeSounds, Is.EqualTo(soundsBefore), "a living player hears nothing"));
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore), "and sees nothing"));
    }

    /// <summary>
    ///     Ghost roles are quiet by default, antagonists included: only the marker makes one notable.
    /// </summary>
    [Test]
    public async Task UnmarkedRoleIsNotAnnouncedEvenAsAnAntagonist()
    {
        await PossessAsync(Ghost);
        var noticesBefore = ClientNotice.ReceivedCount;

        await SpawnRolesAsync(UnmarkedAntagRole);

        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore)));
    }

    /// <summary>
    ///     A shuttle that brings several roles at once makes one sound, with one line per kind of role.
    /// </summary>
    [Test]
    public async Task RolesAppearingTogetherMakeOneSound()
    {
        await PossessAsync(Ghost);
        var soundsBefore = ServerNoticeSounds;
        var noticesBefore = ClientNotice.ReceivedCount;

        await SpawnRolesAsync(NotableRole, NotableRole, NotableRole, OtherNotableRole);

        await Server.WaitAssertion(() => Assert.That(ServerNoticeSounds, Is.EqualTo(soundsBefore + 1), "one sound for the lot"));
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore + 2), "one line per role name"));
    }

    /// <summary>
    ///     A role taken in the tick it appears, as a ghost role mob spawner hands its mob over, was never up for grabs.
    /// </summary>
    [Test]
    public async Task RoleTakenAsItAppearsIsNotAnnounced()
    {
        await PossessAsync(Ghost);
        var noticesBefore = ClientNotice.ReceivedCount;

        // Someone else's mind, so the test player stays the ghost who would hear the notice.
        await Server.WaitPost(() =>
        {
            var mindSystem = SEntMan.System<SharedMindSystem>();
            mindSystem.TransferTo(mindSystem.CreateMind(null), SEntMan.SpawnEntity(NotableRole, MapCoordinates.Nullspace));
        });
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore)));
    }

    /// <summary>
    ///     A random humanoid spawner (an ERT member, a pirate) adds its settings' components to the mob it makes.
    /// </summary>
    [Test]
    public async Task NotableRandomHumanoidIsAnnounced()
    {
        var testMap = await Pair.CreateTestMap();
        await PossessAsync(Ghost);
        var noticesBefore = ClientNotice.ReceivedCount;

        // On a map, not in nullspace: the spawner makes the humanoid at its own coordinates.
        await Server.WaitPost(() => SEntMan.SpawnAtPosition(NotableHumanoidSpawner, testMap.GridCoords));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() => Assert.That(SEntMan.EntityQuery<KsGhostRoleNotableComponent>().Count(), Is.GreaterThan(0), "precondition: the humanoid was made"));
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore + 1)));
    }

    [Test]
    public async Task AdminMadeRoleIsAnnounced()
    {
        await PossessAsync(Ghost);
        var noticesBefore = ClientNotice.ReceivedCount;

        EntityUid target = default;
        await Server.WaitPost(() => target = SEntMan.SpawnEntity(LivingMob, MapCoordinates.Nullspace));
        var consoleHost = Server.ResolveDependency<IConsoleHost>();
        await Server.WaitPost(() => consoleHost.ExecuteCommand($"makeghostrole {SEntMan.GetNetEntity(target)} Test Description"));
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore + 1)));
    }

    [Test]
    public async Task AdminMadeRaffledRoleIsAnnounced()
    {
        await PossessAsync(Ghost);
        var noticesBefore = ClientNotice.ReceivedCount;

        EntityUid target = default;
        await Server.WaitPost(() => target = SEntMan.SpawnEntity(LivingMob, MapCoordinates.Nullspace));
        var consoleHost = Server.ResolveDependency<IConsoleHost>();
        await Server.WaitPost(() => consoleHost.ExecuteCommand($"makeghostroleraffled {SEntMan.GetNetEntity(target)} Test Description short"));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<GhostRoleComponent>(target), "precondition: the command worked"));
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore + 1)));
    }
}

/// <summary>
///     Roles that exist before the round starts are never announced, however notable.
/// </summary>
[TestFixture]
public sealed class KsGhostRoleNoticePreRoundTests : KsGhostRoleNoticeTestBase
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId NotableSpawner = "SpawnPointGhostDragon";
    private static readonly EntProtoId HordeMob = "MobAdultSlimesBlueAngry";
    private static readonly ProtoId<RandomHumanoidSettingsPrototype> ErtMember = "ERTSecurityArmedShotgun";

    [Test]
    public async Task RolesBeforeTheRoundStartsAreNotAnnounced()
    {
        await PossessAsync(Ghost);
        var noticesBefore = ClientNotice.ReceivedCount;

        await SpawnRolesAsync(NotableRole, OtherNotableRole);

        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore)));
    }

    /// <summary>
    ///     The partials that mark upstream roles notable apply, reach the children of a marked parent, and leave the
    ///         rest of the role alone.
    /// </summary>
    [Test]
    public async Task NotablePartialsApplyAndLeaveHordesQuiet()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;
            var spawner = SProtoMan.Index(NotableSpawner);
            Assert.That(spawner.TryGetComponent<KsGhostRoleNotableComponent>(out _, factory), "the dragon is notable");
            Assert.That(spawner.TryGetComponent<GhostRoleComponent>(out _, factory), "and is still a ghost role");

            var ertComponents = SProtoMan.Index(ErtMember).Components;
            Assert.That(ertComponents, Is.Not.Null);
            Assert.That(ertComponents!.ContainsKey(factory.GetComponentName<KsGhostRoleNotableComponent>()), "an ERT member inherits the mark from the Central Command settings");
            Assert.That(ertComponents.ContainsKey(factory.GetComponentName<GhostRoleComponent>()), "and keeps its ghost role");

            Assert.That(SProtoMan.Index(HordeMob).TryGetComponent<KsGhostRoleNotableComponent>(out _, factory), Is.False, "vent horde slimes are quiet");
        });
    }
}
