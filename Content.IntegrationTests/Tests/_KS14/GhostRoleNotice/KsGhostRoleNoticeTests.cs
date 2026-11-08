#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
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
    protected readonly EntProtoId FlaggedRole = "KsGhostRoleNoticeTestFlagged";
    protected readonly EntProtoId PlainRole = "KsGhostRoleNoticeTestPlain";
    protected readonly EntProtoId LivingMob = "KsGhostRoleNoticeTestMob";
    protected readonly EntProtoId Ghost = "MobObserver";

    [TestPrototypes]
    protected const string Prototypes = @"
- type: entity
  id: KsGhostRoleNoticeTestAntag
  components:
  - type: GhostRole
    name: ks-test-role
    description: ks-test-role
    mindRoles:
    - MindRoleGhostRoleSoloAntagonist

- type: entity
  id: KsGhostRoleNoticeTestFlagged
  components:
  - type: GhostRole
    name: ks-test-role
    description: ks-test-role
    notable: true

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

        await Pair.RunTicksSync(3);
    }

    protected async Task SpawnRoleAsync(EntProtoId proto)
    {
        await Server.WaitPost(() => SEntMan.SpawnEntity(proto, MapCoordinates.Nullspace));
        await Pair.RunTicksSync(3);
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
    public async Task FlaggedRoleIsAnnouncedButAPlainOneIsNot()
    {
        await PossessAsync(Ghost);

        var noticesBefore = ClientNotice.ReceivedCount;
        await SpawnRoleAsync(PlainRole);
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore), "an ordinary role such as a mouse is not announced"));

        await SpawnRoleAsync(FlaggedRole);
        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore + 1), "a flagged one is"));
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
        await Pair.RunTicksSync(3);

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
        await SpawnRoleAsync(FlaggedRole);

        await Client.WaitAssertion(() => Assert.That(ClientNotice.ReceivedCount, Is.EqualTo(noticesBefore)));
    }
}
