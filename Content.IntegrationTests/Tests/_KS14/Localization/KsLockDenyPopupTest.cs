#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.CCVar;
using Content.Shared.Lock;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Localization;

/// <summary>
///     A lock's "access denied" popup is localized by the player who sees it, not formatted in the server's English
///         beside the client's own (issue #474).
/// </summary>
[TestOf(typeof(LockSystem))]
public sealed class KsLockDenyPopupTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Locker = "LockerCaptain";
    private static readonly EntProtoId Human = "MobHuman";
    private const string DenyMessage = "lock-comp-has-user-access-fail";

    [Test]
    public async Task AccessDeniedShowsOnceInThePlayersLanguage()
    {
        var map = await Pair.CreateTestMap();
        var locker = EntityUid.Invalid;
        var user = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            locker = SEntMan.SpawnEntity(Locker, map.GridCoords);
            user = SEntMan.SpawnEntity(Human, map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
        });
        await RunUntilSynced();

        var russian = "";
        await Client.WaitPost(() =>
        {
            Client.ResolveDependency<IConfigurationManager>().SetCVar(KsCCVars.ClientLocale, "ru-RU");
            russian = Client.ResolveDependency<ILocalizationManager>().GetString(DenyMessage);
        });
        var english = "";
        await Server.WaitPost(() =>
        {
            english = Server.ResolveDependency<ILocalizationManager>().GetString(DenyMessage);
            Assert.That(SEntMan.System<LockSystem>().HasUserAccess(locker, user, quiet: false), Is.False,
                "a mob with no ID must be refused by the captain's locker");
        });
        Assert.That(russian, Is.Not.EqualTo(english));

        await Pair.RunTicksSync(5);
        await Client.WaitPost(() =>
        {
            var labels = CEntMan.System<Content.Client.Popups.PopupSystem>().WorldLabels.Select(label => label.Text).ToList();
            Assert.That(labels, Does.Contain(russian));
            Assert.That(labels, Does.Not.Contain(english), "the server's English copy of the popup reached the client");
        });
    }
}
