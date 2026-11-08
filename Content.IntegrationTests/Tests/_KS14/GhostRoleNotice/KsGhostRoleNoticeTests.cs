#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
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

    protected readonly EntProtoId AntagRole = "KsGhostRoleNoticeTestAntag";
    protected readonly EntProtoId QuietRole = "KsGhostRoleNoticeTestQuiet";
    protected readonly EntProtoId PlainRole = "KsGhostRoleNoticeTestPlain";
    protected readonly EntProtoId LivingMob = "KsGhostRoleNoticeTestMob";
    protected readonly EntProtoId ToggleMob = "KsGhostRoleNoticeTestToggle";
    protected readonly EntProtoId Ghost = "MobObserver";

    [TestPrototypes]
    protected const string Prototypes = @"
- type: entity
  id: KsGhostRoleNoticeTestAntag
  components:
  - type: KsGhostRoleQuiet
  - type: GhostRole
    name: ks-test-role
    description: ks-test-role
    mindRoles:
    - MindRoleGhostRoleSoloAntagonist

- type: entity
  id: KsGhostRoleNoticeTestQuiet
  components:
  - type: KsGhostRoleQuiet
  - type: GhostRole
    name: ks-test-role
    description: ks-test-role

- type: entity
  id: KsGhostRoleNoticeTestToggle
  components:
  - type: MindContainer
  - type: ToggleableGhostRole
    roleName: ks-test-role
    roleDescription: ks-test-role
    mindRoles:
    - MindRoleGhostRoleNeutral

- type: entity
  id: KsGhostRoleNoticeTestPlain
  components:
  - type: GhostRole
    name: ks-test-role
    description: ks-test-role

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
        await Server.WaitPost(() =>
        {
            var mob = SEntMan.SpawnEntity(proto, MapCoordinates.Nullspace);
            var mindSystem = SEntMan.System<SharedMindSystem>();
            mindSystem.TransferTo(mindSystem.CreateMind(ServerSession!.UserId), mob);
        });

        await Pair.RunTicksSync(10);
    }

    protected async Task SpawnRoleAsync(EntProtoId proto)
    {
        await Server.WaitPost(() => SEntMan.SpawnEntity(proto, MapCoordinates.Nullspace));
        await Pair.RunTicksSync(10);
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
    public async Task AntagRoleReachesGhostsAndNotTheLiving()
    {
        await PossessAsync(Ghost);
        var soundsBefore = ServerNoticeSounds;
        var noticesBefore = ClientNotice.ReceivedCount;

        await SpawnRoleAsync(AntagRole);

        await Server.WaitAssertion(() => Assert.That(ServerNoticeSounds, Is.GreaterThan(soundsBefore), "the sound is played for the ghost"));
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore + 1), "and so is the chat line"));

        await PossessAsync(LivingMob);
        soundsBefore = ServerNoticeSounds;
        noticesBefore = ClientNotice.ReceivedCount;

        await SpawnRoleAsync(AntagRole);

        await Server.WaitAssertion(() => Assert.That(ServerNoticeSounds, Is.EqualTo(soundsBefore), "a living player hears nothing"));
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore), "and sees nothing"));
    }

    [Test]
    public async Task PlainRoleIsAnnouncedButAQuietOneIsNot()
    {
        await PossessAsync(Ghost);

        var noticesBefore = ClientNotice.ReceivedCount;
        await SpawnRoleAsync(QuietRole);
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore), "a role marked quiet is not announced"));

        await SpawnRoleAsync(PlainRole);
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore + 1), "an unmarked one is"));
    }

    [Test]
    public async Task RuntimeAddedQuietRoleIsNotAnnounced()
    {
        await PossessAsync(Ghost);
        var noticesBefore = ClientNotice.ReceivedCount;

        EntityUid toggleMob = default;
        await Server.WaitPost(() =>
        {
            toggleMob = SEntMan.SpawnEntity(ToggleMob, MapCoordinates.Nullspace);
            SEntMan.System<ToggleableGhostRoleSystem>().ActivateGhostRole(toggleMob);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<GhostRoleComponent>(toggleMob), "precondition: it became a ghost role"));
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore), "pAI-like toggles stay silent"));
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

    [Test]
    public async Task RolesBeforeTheRoundStartsAreNotAnnounced()
    {
        await PossessAsync(Ghost);
        var noticesBefore = ClientNotice.ReceivedCount;

        await SpawnRoleAsync(AntagRole);
        await SpawnRoleAsync(PlainRole);

        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore)));
    }

    private readonly EntProtoId _realQuiet = "MobMouse";
    private readonly EntProtoId _realLoud = "MobXeno";

    /// <summary>
    ///     The partial that marks upstream animals quiet applies, and leaves the rest of their ghost role alone.
    /// </summary>
    [Test]
    public async Task QuietPartialAppliesToMiceAndNotToXenos()
    {
        await Server.WaitAssertion(() =>
        {
            var mouse = SProtoMan.Index(_realQuiet);
            Assert.That(mouse.TryGetComponent<Content.Shared._KS14.GhostRoleNotice.KsGhostRoleQuietComponent>(out _, SEntMan.ComponentFactory), "mice are quiet");
            Assert.That(mouse.TryGetComponent<GhostRoleComponent>(out _, SEntMan.ComponentFactory), "and are still ghost roles");
            Assert.That(SProtoMan.Index(_realLoud).TryGetComponent<Content.Shared._KS14.GhostRoleNotice.KsGhostRoleQuietComponent>(out _, SEntMan.ComponentFactory), Is.False, "xenos are not");
        });
    }
}
