using System.Linq;
using Content.Client.Options.UI;
using Content.Client.Options.UI.Tabs;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Richtext;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Examine;
using Content.Shared.Guidebook;
using Content.Shared.Localizations;
using Content.Shared.RCD.Components;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.RadioLocalization;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Localization;

public sealed class KsClientLocalizationTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task ClientLocaleChangesCachedPrototypeNamesWithoutChangingServerCulture()
    {
        var configurationManager = Client.ResolveDependency<IConfigurationManager>();
        var localizationManager = Client.ResolveDependency<ILocalizationManager>();
        var englishName = "";
        var russianName = "";
        var restoredName = "";
        var formattedEnergy = "";
        var localeOptionCount = 0;
        var installedCultureCount = 0;
        string[] installedCultures = [];

        await Client.WaitPost(() =>
        {
            configurationManager.SetCVar(KsCCVars.ClientLocale, "en-US");
            englishName = localizationManager.GetEntityData("Wrench").Name;
            configurationManager.SetCVar(KsCCVars.ClientLocale, "ru-RU");
            russianName = localizationManager.GetEntityData("Wrench").Name;
            foreach (var prototype in Client.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<EntityPrototype>())
                localizationManager.GetEntityData(prototype.ID);
            formattedEnergy = localizationManager.GetString("zzzz-fmt-energy-watt-hours",
                ("divided", 1.5), ("places", 0));
            Assert.That(localizationManager.GetString("apc-menu-power-state-label-text", ("power", 1500)),
                Does.Contain("Вт"));
            var tab = new Ks14Tab();
            localeOptionCount = tab.FindControl<OptionDropDown>("DropDownClientLocale").Button.ItemCount;
            installedCultureCount = Client.ResolveDependency<ContentLocalizationManager>().GetAvailableCultures().Count;
            installedCultures = Client.ResolveDependency<ContentLocalizationManager>().GetAvailableCultures()
                .Select(culture => culture.Name).ToArray();
            configurationManager.SetCVar(KsCCVars.ClientLocale, "en-US");
            restoredName = localizationManager.GetEntityData("Wrench").Name;
        });

        Assert.Multiple(() =>
        {
            Assert.That(englishName, Is.Not.Empty);
            Assert.That(russianName, Is.Not.Empty.And.Not.EqualTo(englishName));
            Assert.That(restoredName, Is.EqualTo(englishName), "changing culture must invalidate entity-name caches");
            Assert.That(formattedEnergy, Does.Contain("Вт"), "content formatting functions must be registered for Russian");
            Assert.That(localeOptionCount, Is.EqualTo(installedCultureCount));
            Assert.That(installedCultures, Does.Not.Contain("pt-BR").And.Not.Contain("nl-NL"));
            Assert.That(Server.ResolveDependency<ILocalizationManager>().DefaultCulture?.Name, Is.EqualTo("en-US"));
        });
    }

    [Test]
    public async Task AddedCultureWorksInSettingsFormattingGuidebookAndServerExamine()
    {
        const string frenchOptions = "ks-ui-options-client-locale = Langue du client";
        const string frenchLocale = """
            ent-Wrench = clé
                .desc = Une clé française.
            ent-BulletLaserSpreadNarrow = rafale laser létale
                .desc = { "" }
            gun-set-fire-mode-examine = Mode choisi : { $mode }.
            ks-test-locale-number = { NATURALFIXED($value, 2) }
            """;
        var serverResources = new MemoryContentRoot();
        var clientResources = new MemoryContentRoot();
        var testMap = await Pair.CreateTestMap();
        var gunNetEntity = NetEntity.Invalid;
        ExamineSystemMessages.ExamineInfoResponseMessage? response = null;
        EventHandler<object> capture = (_, message) =>
        {
            if (message is ExamineSystemMessages.ExamineInfoResponseMessage examineResponse)
                response = examineResponse;
        };
        var configurationManager = Client.ResolveDependency<IConfigurationManager>();
        var localizationManager = Client.ResolveDependency<ILocalizationManager>();
        var contentLocalizationManager = Client.ResolveDependency<ContentLocalizationManager>();
        var selectedCulture = "";
        var canonicalSetting = "";
        var wrenchName = "";
        var formattedNumber = "";
        var optionCount = 0;
        var installedCount = 0;
        var guideText = "";
        var rejectedClientCulture = "";
        var restoredServerCulture = "";
        try
        {
            await Server.WaitPost(() =>
            {
                serverResources.AddOrUpdateFile(new ResPath("Locale/fr-FR/test.ftl"), frenchLocale);
                serverResources.AddOrUpdateFile(new ResPath("Locale/fr-FR/_KS14/Localization/options.ftl"), frenchOptions);
                Server.ResolveDependency<IResourceManager>().AddRoot(new ResPath("/"), serverResources);
                Server.ResolveDependency<ContentLocalizationManager>().RefreshAvailableCultures();
                var observerUid = SEntMan.SpawnEntity("MobObserver", testMap.GridCoords);
                Server.PlayerMan.SetAttachedEntity(ServerSession!, observerUid);
                var gunUid = SEntMan.SpawnEntity("WeaponEnergyShotgun", testMap.GridCoords);
                gunNetEntity = SEntMan.GetNetEntity(gunUid);
                Server.ResolveDependency<ILocalizationManager>().GetEntityData("BulletLaserSpreadNarrow");
            });
            await RunUntilSynced();
            await Client.WaitPost(() =>
            {
                clientResources.AddOrUpdateFile(new ResPath("Locale/fr-FR/test.ftl"), frenchLocale);
                clientResources.AddOrUpdateFile(new ResPath("Locale/fr-FR/_KS14/Localization/options.ftl"), frenchOptions);
                var power = Client.ResolveDependency<IPrototypeManager>().Index<GuideEntryPrototype>("Power");
                var relative = power.Text.ToString().TrimStart('/')[11..];
                clientResources.AddOrUpdateFile(new ResPath($"ServerInfo/_KS14/Guidebook/fr/{relative}"), "<Document># Électricité</Document>");
                Client.ResolveDependency<IResourceManager>().AddRoot(new ResPath("/"), clientResources);
                contentLocalizationManager.RefreshAvailableCultures();
                configurationManager.SetCVar(KsCCVars.ClientLocale, "fr-fr");
                selectedCulture = localizationManager.DefaultCulture!.Name;
                canonicalSetting = configurationManager.GetCVar(KsCCVars.ClientLocale);
                wrenchName = localizationManager.GetEntityData("Wrench").Name;
                formattedNumber = localizationManager.GetString("ks-test-locale-number", ("value", 1234.5));
                var tab = new Ks14Tab();
                optionCount = tab.FindControl<OptionDropDown>("DropDownClientLocale").Button.ItemCount;
                installedCount = contentLocalizationManager.GetAvailableCultures().Count;
                var path = Client.ResolveDependency<DocumentParsingManager>().GetLocalizedDocumentPath(power.Text);
                using var reader = Client.ResolveDependency<IResourceManager>().ContentFileReadText(path);
                guideText = reader.ReadToEnd();
                CEntMan.EntityNetManager.ReceivedSystemMessage += capture;
                CEntMan.EntityNetManager.SendSystemNetworkMessage(new ExamineSystemMessages.RequestExamineInfoMessage(gunNetEntity, 1234)
                {
                    ClientLocale = "fr-FR",
                });
            });
            await Pair.RunTicksSync(10);
            Assert.Multiple(() =>
            {
                Assert.That(selectedCulture, Is.EqualTo("fr-FR"));
                Assert.That(canonicalSetting, Is.EqualTo("fr-FR"));
                Assert.That(wrenchName, Is.EqualTo("clé"));
                Assert.That(formattedNumber, Does.Contain(",5"));
                Assert.That(optionCount, Is.EqualTo(installedCount).And.GreaterThanOrEqualTo(3));
                Assert.That(guideText, Does.Contain("Électricité"));
                Assert.That(response, Is.Not.Null);
                Assert.That(response!.Message.ToString(), Does.Contain("Mode choisi : rafale laser létale"));
            });
            await Client.WaitPost(() =>
            {
                response = null;
                CEntMan.EntityNetManager.SendSystemNetworkMessage(new ExamineSystemMessages.RequestExamineInfoMessage(gunNetEntity, 1235)
                {
                    ClientLocale = "../../ru-RU",
                });
                configurationManager.SetCVar(KsCCVars.ClientLocale, "../../ru-RU");
                rejectedClientCulture = configurationManager.GetCVar(KsCCVars.ClientLocale);
            });
            await Pair.RunTicksSync(10);
            await Server.WaitPost(() => restoredServerCulture = Server.ResolveDependency<ILocalizationManager>().DefaultCulture!.Name);
            Assert.That(rejectedClientCulture, Is.EqualTo("en-US"));
            Assert.That(restoredServerCulture, Is.EqualTo("en-US"));
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Message.ToString(), Does.Contain("lethal laser barrage").And.Not.Contain("rafale laser létale"));
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                CEntMan.EntityNetManager.ReceivedSystemMessage -= capture;
                configurationManager.SetCVar(KsCCVars.ClientLocale, "en-US");
                clientResources.Clear();
                contentLocalizationManager.RefreshAvailableCultures();
            });
            await Server.WaitPost(() =>
            {
                serverResources.Clear();
                Server.ResolveDependency<ContentLocalizationManager>().RefreshAvailableCultures();
            });
        }
    }

    [Test]
    public async Task AuthoritativeExamineResponseUsesClientLocaleAndRestoresServerCulture()
    {
        var testMap = await Pair.CreateTestMap();
        var gunNetEntity = NetEntity.Invalid;
        var expectedDescription = "";
        ExamineSystemMessages.ExamineInfoResponseMessage? response = null;
        EventHandler<object> capture = (_, message) =>
        {
            if (message is ExamineSystemMessages.ExamineInfoResponseMessage examineResponse)
                response = examineResponse;
        };

        await Server.WaitPost(() =>
        {
            var observerUid = SEntMan.SpawnEntity("MobObserver", testMap.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, observerUid);
            var gunUid = SEntMan.SpawnEntity("WeaponPistolMk58", testMap.GridCoords);
            gunNetEntity = SEntMan.GetNetEntity(gunUid);
        });
        await RunUntilSynced();
        await Client.WaitPost(() =>
        {
            Client.ResolveDependency<IConfigurationManager>().SetCVar(KsCCVars.ClientLocale, "ru-RU");
            expectedDescription = Client.ResolveDependency<ILocalizationManager>().GetEntityData("WeaponPistolMk58").Desc;
            CEntMan.EntityNetManager.ReceivedSystemMessage += capture;
            CEntMan.EntityNetManager.SendSystemNetworkMessage(new ExamineSystemMessages.RequestExamineInfoMessage(gunNetEntity, 1234)
            {
                ClientLocale = "ru-RU",
            });
        });
        await Pair.RunTicksSync(10);
        await Client.WaitPost(() =>
        {
            Assert.That(response, Is.Not.Null, "check the server's response, not the client preview");
            Assert.That(expectedDescription, Is.Not.Empty);
            Assert.That(response!.Message.ToString(), Does.Contain(expectedDescription));
            Assert.That(response.Message.ToString(), Does.Contain("Скорострельность"));
            response = null;
            CEntMan.EntityNetManager.SendSystemNetworkMessage(new ExamineSystemMessages.RequestExamineInfoMessage(gunNetEntity, 1235)
            {
                ClientLocale = "en-US",
            });
        });
        await Pair.RunTicksSync(10);
        await Client.WaitPost(() =>
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Message.ToString(), Does.Contain("Fire rate"));
            CEntMan.EntityNetManager.ReceivedSystemMessage -= capture;
        });
        await Server.WaitPost(() => Assert.That(Server.ResolveDependency<ILocalizationManager>().DefaultCulture?.Name,
            Is.EqualTo("en-US")));
    }

    [Test]
    public async Task NestedNameResolverPreservesCustomNamesAndEnglishServerCache()
    {
        await Server.WaitPost(() =>
        {
            var localizationManager = Server.ResolveDependency<ILocalizationManager>();
            var contentLocalizationManager = Server.ResolveDependency<ContentLocalizationManager>();
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<EntityPrototype>("Wrench");
            var englishName = prototype.Name;
            var itemUid = SEntMan.SpawnEntity("Wrench", Robust.Shared.Map.MapCoordinates.Nullspace);
            var russianCulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            contentLocalizationManager.LoadAdditionalCulture(russianCulture);
            using (new Content.Server._KS14.Localization.KsExamineLocaleScope(localizationManager, russianCulture))
            {
                var russianName = contentLocalizationManager.GetLocalizedEntityName(itemUid, SEntMan, englishName);
                Assert.That(russianName, Is.Not.Empty.And.Not.EqualTo(englishName));
                SEntMan.System<MetaDataSystem>().SetEntityName(itemUid, "My custom wrench");
                Assert.That(contentLocalizationManager.GetLocalizedEntityName(itemUid, SEntMan,
                    SEntMan.GetComponent<MetaDataComponent>(itemUid).EntityName), Is.EqualTo("My custom wrench"));
                Assert.That(prototype.Name, Is.EqualTo(englishName), "the server's cache must remain English");
            }
            Assert.That(localizationManager.DefaultCulture?.Name, Is.EqualTo("en-US"));
        });
    }

    [TestCase("RCD", "KsPlumbingTeleporterBeacon", "KsPlumbingTeleporterBeacon", "химический маяк", "chemical beacon")]
    [TestCase("RPD", "PipeStraight", "GasPipeStraight", "газовая труба", "gas pipe")]
    [TestCase("WeaponEnergyShotgun", null, "BulletLaserSpreadNarrow", "летальный лазерный залп", "lethal laser barrage")]
    [TestCase("AutolatheMachineCircuitboard", null, "SheetGlass1", "стекло", "glass")]
    public async Task NestedPrototypeNameStaysRussianAfterServerResponse(string itemPrototype, string? constructionMode,
        string nestedPrototype, string russianName, string englishName)
    {
        var testMap = await Pair.CreateTestMap();
        var rcdNetEntity = NetEntity.Invalid;
        var expectedName = "";
        ExamineSystemMessages.ExamineInfoResponseMessage? response = null;
        EventHandler<object> capture = (_, message) =>
        {
            if (message is ExamineSystemMessages.ExamineInfoResponseMessage examineResponse)
                response = examineResponse;
        };

        await Server.WaitPost(() =>
        {
            var observerUid = SEntMan.SpawnEntity("MobObserver", testMap.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, observerUid);
            var rcdUid = SEntMan.SpawnEntity(itemPrototype, testMap.GridCoords);
            if (constructionMode != null)
            {
#pragma warning disable RA0002 // Set the construction mode directly for this regression fixture.
                SEntMan.GetComponent<RCDComponent>(rcdUid).ProtoId = constructionMode;
#pragma warning restore RA0002
            }
            rcdNetEntity = SEntMan.GetNetEntity(rcdUid);
            // This English cache is present in production before the Russian examine request.
            Assert.That(Server.ResolveDependency<ILocalizationManager>().GetEntityData(nestedPrototype).Name,
                Is.EqualTo(englishName));
        });
        await RunUntilSynced();
        await Client.WaitPost(() =>
        {
            Client.ResolveDependency<IConfigurationManager>().SetCVar(KsCCVars.ClientLocale, "ru-RU");
            expectedName = Client.ResolveDependency<ContentLocalizationManager>().GetLocalizedPrototypeName(
                Client.ResolveDependency<IPrototypeManager>().Index<EntityPrototype>(nestedPrototype));
            CEntMan.EntityNetManager.ReceivedSystemMessage += capture;
            CEntMan.EntityNetManager.SendSystemNetworkMessage(new ExamineSystemMessages.RequestExamineInfoMessage(rcdNetEntity, 1234)
            {
                ClientLocale = "ru-RU",
            });
        });
        await Pair.RunTicksSync(10);
        // Assert on the NUnit thread so a pooled game thread cannot attribute a
        // failure to a previous parameterized test case.
        Assert.That(response, Is.Not.Null);
        Assert.That(expectedName, Is.EqualTo(russianName));
        Assert.That(response!.Message.ToString(), Does.Contain(expectedName).And.Not.Contain(englishName));
        await Client.WaitPost(() =>
        {
            response = null;
            CEntMan.EntityNetManager.SendSystemNetworkMessage(new ExamineSystemMessages.RequestExamineInfoMessage(rcdNetEntity, 1235)
            {
                ClientLocale = "en-US",
            });
        });
        await Pair.RunTicksSync(10);
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Message.ToString(), Does.Contain(englishName));
        await Client.WaitPost(() =>
        {
            CEntMan.EntityNetManager.ReceivedSystemMessage -= capture;
        });
        await Server.WaitPost(() => Assert.That(Server.ResolveDependency<ILocalizationManager>().DefaultCulture?.Name,
            Is.EqualTo("en-US")));
    }

    [Test]
    public async Task RussianGuideDocumentsResolveAndParseInClient()
    {
        await Client.WaitPost(() =>
        {
            var configurationManager = Client.ResolveDependency<IConfigurationManager>();
            var parser = Client.ResolveDependency<DocumentParsingManager>();
            var resourceManager = Client.ResolveDependency<IResourceManager>();
            var prototypeManager = Client.ResolveDependency<IPrototypeManager>();
            configurationManager.SetCVar(KsCCVars.ClientLocale, "ru-RU");
            foreach (var guidePrototype in prototypeManager.EnumeratePrototypes<GuideEntryPrototype>())
            {
                var path = parser.GetLocalizedDocumentPath(guidePrototype.Text);
                Assert.That(path, Is.Not.EqualTo(guidePrototype.Text), guidePrototype.ID);
                using var reader = resourceManager.ContentFileReadText(path);
                Assert.That(parser.TryAddMarkup(new Document(), reader.ReadToEnd()), Is.True, guidePrototype.ID);
            }
            var power = prototypeManager.Index<GuideEntryPrototype>("Power");
            using var powerReader = resourceManager.ContentFileReadText(parser.GetLocalizedDocumentPath(power.Text));
            Assert.That(powerReader.ReadToEnd(), Does.Contain("Электропитание"));
            var anchorless = prototypeManager.Index<GuideEntryPrototype>("Anchorless");
            using var anchorlessReader = resourceManager.ContentFileReadText(parser.GetLocalizedDocumentPath(anchorless.Text));
            var anchorlessText = anchorlessReader.ReadToEnd();
            Assert.That(anchorlessText, Does.Contain("# Безъякорные").And.Not.Contain("Anchorless"));
            Assert.That(Client.ResolveDependency<ILocalizationManager>().GetString(anchorless.Name), Is.EqualTo("Безъякорные"));
            configurationManager.SetCVar(KsCCVars.ClientLocale, "en-US");
            Assert.That(parser.GetLocalizedDocumentPath(power.Text), Is.EqualTo(power.Text));
        });
    }

    [Test]
    public async Task RadioAliasesAreLocaleSpecificAndLeaveMessageContentsUntouched()
    {
        await Client.WaitPost(() =>
        {
            var prototypeManager = Client.ResolveDependency<IPrototypeManager>();
            Assert.Multiple(() =>
            {
                Assert.That(KsRadioLocalization.NormalizePrefix(prototypeManager, "ru-RU", ":и Привет :и"),
                    Is.EqualTo(":e Привет :и"));
                Assert.That(KsRadioLocalization.NormalizePrefix(prototypeManager, "ru-RU", ".И HELLO"),
                    Is.EqualTo(".e HELLO"));
                Assert.That(KsRadioLocalization.NormalizePrefix(prototypeManager, "en-US", ":и Привет"),
                    Is.EqualTo(":и Привет"));
                Assert.That(KsRadioLocalization.NormalizePrefix(prototypeManager, "ru-RU", ":e hello"),
                    Is.EqualTo(":e hello"));
                Assert.That(KsRadioLocalization.NormalizePrefix(prototypeManager, "ru-RU", "; Привет"),
                    Is.EqualTo("; Привет"));
                Assert.That(KsRadioLocalization.GetLocalizedKey(prototypeManager, "ru-RU", 'e'), Is.EqualTo('и'));
            });
        });
    }
}
