#nullable enable
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Popups;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Prototypes;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Medical.SuitSensors;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Radio.EntitySystems;
using Content.Shared.Localizations;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Localization;

public sealed class KsPopupLocalizationTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestCase("sensor", "ClothingUniformJumpsuitEngineering")]
    [TestCase("radio", "RadioHandheld")]
    [TestCase("injector", "Syringe")]
    [TestCase("flavours", "Wrench")]
    public async Task NestedDevicePopupUsesRecipientCulture(string feature, string prototypeId)
    {
        var map = await Pair.CreateTestMap();
        var actor = EntityUid.Invalid;
        var device = EntityUid.Invalid;
        var deviceNet = NetEntity.Invalid;
        var expected = "";
        await Server.WaitPost(() =>
        {
            actor = SEntMan.SpawnEntity("MobObserver", map.GridCoords);
            device = SEntMan.SpawnEntity(prototypeId, map.GridCoords);
            deviceNet = SEntMan.GetNetEntity(device);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, actor);
            if (feature == "flavours")
                SEntMan.EnsureComponent<FlavorProfileComponent>(device).Flavors.UnionWith(["sweet", "fruity", "sour"]);
        });
        await RunUntilSynced();
        await Client.WaitPost(() =>
        {
            Client.ResolveDependency<IConfigurationManager>().SetCVar(KsCCVars.ClientLocale, "ru-RU");
            var loc = Client.ResolveDependency<ILocalizationManager>();
            var clientDevice = CEntMan.GetEntity(deviceNet);
            if (feature == "flavours")
                CEntMan.EnsureComponent<FlavorProfileComponent>(clientDevice).Flavors.UnionWith(["sweet", "fruity", "sour"]);
            expected = feature switch
            {
                "sensor" => loc.GetString("suit-sensor-mode-state", ("mode", loc.GetString("suit-sensor-mode-vitals"))),
                "radio" => loc.GetString("handheld-radio-component-on-use", ("radioState", loc.GetString("handheld-radio-component-on-state"))),
                "injector" => loc.GetString("injector-component-mode-changed-text", ("mode", loc.GetString("injector-component-inject-mode-name"))),
                "flavours" => CEntMan.System<FlavorProfileSystem>().GetLocalizedFlavorsMessage(clientDevice,
                    Client.PlayerMan.LocalEntity!.Value, null),
                _ => throw new ArgumentOutOfRangeException(nameof(feature)),
            };
            Assert.That(expected, Does.Match("[А-Яа-яЁё]"));
        });
        await Server.WaitPost(() =>
        {
            switch (feature)
            {
                case "sensor":
                    SEntMan.System<SharedSuitSensorSystem>().SetSensor((device, null), SuitSensorMode.SensorVitals,
                        userUid: actor);
                    break;
                case "radio":
                    SEntMan.System<SharedRadioDeviceSystem>().SetSpeakerEnabled(device, actor, true);
                    break;
                case "injector":
                    SEntMan.System<InjectorSystem>().ToggleMode((device, SEntMan.GetComponent<InjectorComponent>(device)),
                        actor, Server.ResolveDependency<IPrototypeManager>().Index<InjectorModePrototype>("SyringeInjectMode"));
                    break;
                case "flavours":
                    SEntMan.System<Content.Server.Popups.PopupSystem>().PopupEntity(
                        SEntMan.System<FlavorProfileSystem>().GetFlavorsPopupMessage(device, actor, null), device, actor);
                    break;
            }
        });
        await Pair.RunTicksSync(5);
        await Client.WaitPost(() => Assert.That(CEntMan.System<Content.Client.Popups.PopupSystem>().WorldLabels
            .Select(label => label.Text), Does.Contain(expected)));
    }

    [TestCase("ru-RU", 0)]
    [TestCase("ru-RU", 1)]
    [TestCase("en-US", 0)]
    [TestCase("en-US", 1)]
    public async Task EnergyShotgunModePopupUsesRecipientCulture(string culture, int mode)
    {
        var map = await Pair.CreateTestMap();
        var actor = EntityUid.Invalid;
        var gun = EntityUid.Invalid;
        var expected = "";
        var prototypeId = "";
        await Server.WaitPost(() =>
        {
            actor = SEntMan.SpawnEntity("MobObserver", map.GridCoords);
            gun = SEntMan.SpawnEntity("WeaponEnergyShotgun", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, actor);
            prototypeId = SEntMan.GetComponent<BatteryWeaponFireModesComponent>(gun).FireModes[mode].Prototype;
        });
        await RunUntilSynced();
        await Client.WaitPost(() =>
        {
            Client.ResolveDependency<IConfigurationManager>().SetCVar(KsCCVars.ClientLocale, culture);
            var prototype = Client.ResolveDependency<IPrototypeManager>().Index<EntityPrototype>(prototypeId);
            var name = Client.ResolveDependency<ContentLocalizationManager>().GetLocalizedPrototypeName(prototype);
            expected = Client.ResolveDependency<ILocalizationManager>()
                .GetString("gun-set-fire-mode-popup", ("mode", name));
            Assert.That(KsPopupMessage.Create("gun-set-fire-mode-popup",
                ("mode", new KsPopupPrototypeName(prototypeId))).Format(CEntMan), Is.EqualTo(expected),
                "local prediction must resolve the same ammo name as the authoritative popup");
            if (culture == "ru-RU")
                Assert.That(name, Does.Match("[А-Яа-яЁё]"), "the ammo name must also be translated");
        });
        await Server.WaitPost(() => Assert.That(SEntMan.System<BatteryWeaponFireModesSystem>()
            .TrySetFireMode((gun, SEntMan.GetComponent<BatteryWeaponFireModesComponent>(gun)), mode, actor), Is.True));
        await Pair.RunTicksSync(5);
        await Client.WaitPost(() => Assert.That(CEntMan.System<Content.Client.Popups.PopupSystem>().WorldLabels
            .Select(label => label.Text), Does.Contain(expected)));
        await Server.WaitPost(() => Assert.That(Server.ResolveDependency<ILocalizationManager>().DefaultCulture!.Name,
            Is.EqualTo("en-US")));
    }

    [TestCase("ru-RU")]
    [TestCase("en-US")]
    public async Task ServerPopupUsesRecipientCulture(string culture)
    {
        var map = await Pair.CreateTestMap();
        var actor = EntityUid.Invalid;
        var expected = "";
        await Server.WaitPost(() =>
        {
            actor = SEntMan.SpawnEntity("MobObserver", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, actor);
        });
        await RunUntilSynced();
        await Client.WaitPost(() =>
        {
            Client.ResolveDependency<IConfigurationManager>().SetCVar(KsCCVars.ClientLocale, culture);
            expected = Client.ResolveDependency<ILocalizationManager>().GetString("anchorless-convert-begin-message");
        });
        await Server.WaitPost(() => SEntMan.System<Content.Server.Popups.PopupSystem>().PopupEntity(
            KsPopupMessage.Create("anchorless-convert-begin-message"), actor, actor));
        await Pair.RunTicksSync(5);
        await Client.WaitPost(() => Assert.That(CEntMan.System<Content.Client.Popups.PopupSystem>().WorldLabels
            .Select(label => label.Text), Does.Contain(expected)));
        await Server.WaitPost(() => Assert.That(Server.ResolveDependency<ILocalizationManager>().DefaultCulture!.Name,
            Is.EqualTo("en-US")));
    }

    [Test]
    public async Task AuthoritativeLocalizedPopupDoesNotRepeatPrediction()
    {
        var map = await Pair.CreateTestMap();
        var actor = EntityUid.Invalid;
        var target = NetEntity.Invalid;
        var tick = default(GameTick);
        var expected = "";
        await Server.WaitPost(() =>
        {
            actor = SEntMan.SpawnEntity("MobObserver", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, actor);
            target = SEntMan.GetNetEntity(actor);
        });
        await RunUntilSynced();
        await Client.WaitPost(() =>
        {
            Client.ResolveDependency<IConfigurationManager>().SetCVar(KsCCVars.ClientLocale, "ru-RU");
            var popup = CEntMan.System<Content.Client.Popups.PopupSystem>();
            expected = Client.ResolveDependency<ILocalizationManager>().GetString("anchorless-communion-message");
            tick = Client.ResolveDependency<IGameTiming>().CurTick;
            popup.PopupEntity(KsPopupMessage.Create("anchorless-communion-message"), CEntMan.GetEntity(target),
                CEntMan.GetEntity(target));
            Assert.That(popup.WorldLabels.Count(label => label.Text == expected), Is.EqualTo(1));
        });
        await Server.WaitPost(() => SEntMan.EntityNetManager.SendSystemNetworkMessage(new KsLocalizedPopupEvent(
            KsPopupMessage.Create("anchorless-communion-message").ToPayload(SEntMan), KsPopupKind.Entity,
            PopupType.Small, tick, target, default, 0), ServerSession!.Channel));
        await Pair.RunTicksSync(5);
        await Client.WaitPost(() =>
        {
            var labels = CEntMan.System<Content.Client.Popups.PopupSystem>().WorldLabels;
            Assert.That(labels.Count(label => label.Text == expected), Is.EqualTo(1));
            Assert.That(labels.Any(label => label.Text.Contains("x2")), Is.False,
                "server confirmation must be suppressed, not displayed as a repeated popup");
        });
    }

    [Test]
    public async Task MeasurePopupFormattingAndWireCosts()
    {
        const int iterations = 2000;
        var measurements = new List<string>();
        await Server.WaitPost(() =>
        {
            var serializer = Server.ResolveDependency<IRobustSerializer>();
            var entity = SEntMan.SpawnEntity("Wrench", Robust.Shared.Map.MapCoordinates.Nullspace);
            var net = SEntMan.GetNetEntity(entity);
            using var stream = new MemoryStream(4096);
            foreach (var message in new[]
            {
                KsPopupMessage.Create("anchorless-convert-begin-message"),
                KsPopupMessage.Create("gun-ballistic-transfer-invalid", ("ammoEntity", entity), ("targetEntity", entity)),
                KsPopupMessage.Create("gun-selected-mode", ("mode", KsPopupMessage.Create("gun-SemiAuto"))),
            })
            {
                var payload = message.ToPayload(SEntMan);
                var oldText = payload.Format(SEntMan);
                var oldEvent = new PopupEntityEvent(oldText, PopupType.Small, default, net);
                var newEvent = new KsLocalizedPopupEvent(payload, KsPopupKind.Entity, PopupType.Small,
                    default, net, default, 0);
                // Warm formatting and generated serializers before measurement.
                for (var index = 0; index < 20; index++)
                {
                    payload.Format(SEntMan);
                    stream.SetLength(0);
                    serializer.Serialize(stream, oldEvent);
                    stream.SetLength(0);
                    serializer.Serialize(stream, newEvent);
                }
                measurements.Add(Measure(message.Id + " legacy serialization", iterations, () =>
                {
                    stream.SetLength(0);
                    serializer.Serialize(stream, oldEvent);
                }));
                var oldSize = stream.Length;
                measurements.Add(Measure(message.Id + " localized serialization", iterations, () =>
                {
                    stream.SetLength(0);
                    serializer.Serialize(stream, newEvent);
                }));
                var newSize = stream.Length;
                measurements.Add(Measure(message.Id + " payload construction", iterations, () => message.ToPayload(SEntMan)));
                measurements.Add(Measure(message.Id + " receiver formatting", iterations, () => payload.Format(SEntMan)));
                stream.SetLength(0);
                serializer.Serialize(stream, oldEvent);
                for (var index = 0; index < 20; index++)
                {
                    stream.Position = 0;
                    serializer.Deserialize(stream);
                }
                measurements.Add(Measure(message.Id + " legacy deserialization", iterations, () =>
                {
                    stream.Position = 0;
                    serializer.Deserialize(stream);
                }));
                stream.SetLength(0);
                serializer.Serialize(stream, newEvent);
                for (var index = 0; index < 20; index++)
                {
                    stream.Position = 0;
                    serializer.Deserialize(stream);
                }
                measurements.Add(Measure(message.Id + " localized deserialization", iterations, () =>
                {
                    stream.Position = 0;
                    serializer.Deserialize(stream);
                }));
                measurements.Add($"{message.Id}: wire bytes legacy={oldSize}, localized={newSize}");
            }
        });
        foreach (var measurement in measurements)
            TestContext.Out.WriteLine(measurement);
    }

    private static string Measure(string name, int iterations, Action action)
    {
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++)
            action();
        timer.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        return $"{name}: {timer.Elapsed.TotalMilliseconds * 1000 / iterations:F2} us/popup, "
            + $"{allocated / iterations} B/popup ({iterations} iterations)";
    }
}
