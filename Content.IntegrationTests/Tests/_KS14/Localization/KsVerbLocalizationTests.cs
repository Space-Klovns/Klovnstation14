using System.Linq;
using Content.Client.ContextMenu.UI;
using Content.Client.Gameplay;
using Content.Client.Verbs.UI;
using Content.IntegrationTests.Fixtures;
using Content.Shared.RCD.Components;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._KS14.Localization;

public sealed class KsVerbLocalizationTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task TranslatedVerbIsValidatedInClientCultureButCallbackUsesServerCulture()
    {
        var map = await Pair.CreateTestMap();
        var target = NetEntity.Invalid;
        var serverTarget = EntityUid.Invalid;
        KsVerbCultureProbeSystem probe = default!;
        await Server.WaitPost(() =>
        {
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(user);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
            serverTarget = SEntMan.SpawnEntity("Wrench", map.GridCoords);
            target = SEntMan.GetNetEntity(serverTarget);
            probe = SEntMan.System<KsVerbCultureProbeSystem>();
            probe.Target = serverTarget;
            probe.ValidationCulture = null;
            probe.CallbackCulture = null;
        });
        try
        {
            await RunUntilSynced();
            await Client.WaitPost(() =>
            {
                Client.ResolveDependency<IConfigurationManager>().SetCVar(KsCCVars.ClientLocale, "ru-RU");
                CEntMan.EntityNetManager.SendSystemNetworkMessage(new ExecuteVerbEvent(target, new Verb
                {
                    Text = Client.ResolveDependency<ILocalizationManager>().GetString("gun-chamber-bolt-close"),
                }) { ClientLocale = "ru-RU" });
            });
            await Pair.RunTicksSync(10);
            Assert.That(probe.ValidationCulture, Is.EqualTo("ru-RU"));
            Assert.That(probe.CallbackCulture, Is.EqualTo("en-US"), "persistent action effects must use the ordinary server culture");
        }
        finally
        {
            await Server.WaitPost(() => probe.Target = EntityUid.Invalid);
        }
    }

    [TestCase("WeaponRifleLecter", "gun-chamber-bolt-close", "gun-chamber-bolt-open")]
    [TestCase("RPD", "rcd-verb-switch-mode", "rcd-verb-switch-mode")]
    public async Task MenuMergesLocalizedServerVerbsAndExecutesThem(string prototype, string firstKey, string secondKey)
    {
        var map = await Pair.CreateTestMap();
        var target = NetEntity.Invalid;
        var serverEntity = EntityUid.Invalid;
        VerbMenuUIController controller = default!;
        ContextMenuUIController context = default!;
        GameplayState menuState = default!;
        var rpdModeAvailable = true;
        await Server.WaitPost(() =>
        {
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(user);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
            serverEntity = SEntMan.SpawnEntity(prototype, map.GridCoords);
            target = SEntMan.GetNetEntity(serverEntity);
            if (SEntMan.TryGetComponent<ChamberMagazineAmmoProviderComponent>(serverEntity, out var gun))
                SEntMan.System<SharedGunSystem>().SetBoltClosed(serverEntity, gun, false);
            else
            {
                rpdModeAvailable = SEntMan.GetComponent<RCDComponent>(serverEntity).IsRpd;
                SEntMan.System<Content.Server.Hands.Systems.HandsSystem>().TryPickupAnyHand(user, serverEntity);
            }
        });
        await RunUntilSynced();
        Assert.That(rpdModeAvailable, Is.True, "the fixture must be an RPD with pipe modes");
        await Client.WaitPost(() =>
        {
            // The headless fixture does not enter GameplayState, so initialize
            // the real menu controllers and their network response subscription.
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            context = ui.GetUIController<ContextMenuUIController>();
            context.Setup();
            controller = ui.GetUIController<VerbMenuUIController>();
            menuState = new GameplayState();
            controller.OnStateEntered(menuState);
        });
        for (var step = 0; step < 2; step++)
        {
            var expected = "";
            var otherLanguage = "";
            var matchingCount = 0;
            var wrongLanguageCount = 0;
            var authoritative = false;
            var category = "";
            var expectedCategory = "";
            var menuTexts = "";
            var key = step == 0 ? firstKey : secondKey;
            var culture = step == 0 ? "ru-RU" : "en-US";
            await Client.WaitPost(() =>
            {
                var config = Client.ResolveDependency<IConfigurationManager>();
                var localization = Client.ResolveDependency<ILocalizationManager>();
                config.SetCVar(KsCCVars.ClientLocale, step == 0 ? "en-US" : "ru-RU");
                otherLanguage = localization.GetString(key);
                config.SetCVar(KsCCVars.ClientLocale, culture);
                expected = localization.GetString(key);
                category = VerbCategory.Eject.Text;
                expectedCategory = localization.GetString("verb-categories-eject");
                controller.OpenVerbMenu(target);
            });
            await Pair.RunTicksSync(10);
            await Client.WaitPost(() =>
            {
                var matches = controller.CurrentVerbs.Where(verb => verb.Text == expected).ToArray();
                matchingCount = matches.Length;
                wrongLanguageCount = controller.CurrentVerbs.Count(verb => verb.Text == otherLanguage);
                menuTexts = string.Join(", ", controller.CurrentVerbs.Select(verb => verb.Text));
                // Delegates are stripped from the network response: the merged action
                // must come from the authoritative server, not the preliminary client list.
                authoritative = matches.Length == 1 && matches[0].Act == null;
                if (matches.Length == 1)
                    CEntMan.System<Content.Client.Verbs.VerbSystem>().ExecuteVerb(target, matches[0]);
            });
            Assert.That(matchingCount, Is.EqualTo(1), $"Expected one {culture} action '{expected}'. Menu: {menuTexts}");
            Assert.That(wrongLanguageCount, Is.Zero, "the server must not append an English duplicate");
            Assert.That(authoritative, Is.True, "server state must replace the preliminary client action");
            Assert.That(category, Is.EqualTo(expectedCategory), "static categories must follow the selected culture");
            await Pair.RunTicksSync(10);
            var executed = false;
            var serverCulture = "";
            await Server.WaitPost(() =>
            {
                if (SEntMan.TryGetComponent<ChamberMagazineAmmoProviderComponent>(serverEntity, out var gun))
                    executed = gun.BoltClosed == (step == 0);
                else
                    executed = SEntMan.GetComponent<RCDComponent>(serverEntity).CurrentMode
                        == (step == 0 ? RpdMode.Primary : RpdMode.Secondary);
                serverCulture = Server.ResolveDependency<ILocalizationManager>().DefaultCulture!.Name;
            });
            Assert.That(executed, Is.True, "clicking the translated action must perform the server operation");
            Assert.That(serverCulture, Is.EqualTo("en-US"), "requests must restore the ordinary server culture");
        }
        await Client.WaitPost(() =>
        {
            controller.OnStateExited(menuState);
            context.Shutdown();
        });
    }
}
