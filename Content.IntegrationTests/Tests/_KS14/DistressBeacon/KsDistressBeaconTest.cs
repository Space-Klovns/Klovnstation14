using Content.Server._KS14.DistressBeacon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._KS14.DistressBeacon;
using Content.Client._KS14.Graphics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._KS14.FieldCommander;
using Content.Shared._KS14.DistressBeacon;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.DistressBeacon;

/// <summary>
///     A beacon worn on the neck follows its wearer: critical, dead or neither, as they change state and as the beacon
///         is put on and taken off, with its light and its worn sprite in step.
/// </summary>
[TestFixture]
[TestOf(typeof(KsDistressBeaconSystem))]
public sealed class KsDistressBeaconTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // an inventory with a neck slot, and a mob state

    private const string NeckSlot = "neck";

    private static readonly EntProtoId Beacon = "KsDistressBeacon";
    // Not static: shaders are a client-only kind, which the YAML linter does not know.
    private readonly ProtoId<ShaderPrototype> _dissolveShader = "KsDissolve";

    private EntityUid SpawnAndWear()
    {
        var beaconUid = SEntMan.SpawnEntity(Beacon, SEntMan.GetCoordinates(PlayerCoords));
        Assert.That(SEntMan.System<InventorySystem>().TryEquip(SPlayer, beaconUid, NeckSlot, silent: true, force: true), Is.True);
        return beaconUid;
    }

    private MobState State(EntityUid beaconUid)
        => SEntMan.GetComponent<KsDistressBeaconComponent>(beaconUid).State;

    private bool LightOn(EntityUid beaconUid)
        => SEntMan.System<SharedPointLightSystem>().TryGetLight(beaconUid, out var lightComponent) && lightComponent.Enabled;

    private void AssertLightMatches(EntityUid beaconUid, KsDistressBeaconStateSettings settings)
    {
        Assert.That(SEntMan.System<SharedPointLightSystem>().TryGetLight(beaconUid, out var lightComponent), Is.True);
        Assert.That(lightComponent!.Color, Is.EqualTo(settings.LightColor), "The light's colour wasn't the state's.");
        Assert.That(lightComponent.Radius, Is.EqualTo(settings.LightRadius));
        Assert.That(lightComponent.Energy, Is.EqualTo(settings.LightEnergy));
    }

    private void SetMobState(MobState mobState)
        => SEntMan.System<MobStateSystem>().ChangeMobState(SPlayer, mobState);

    [Test]
    public async Task BeaconFollowsItsWearer()
    {
        EntityUid beaconUid = default;
        await Server.WaitPost(() => beaconUid = SpawnAndWear());

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(MobState.Alive), "A beacon on a healthy wearer was on.");
            Assert.That(LightOn(beaconUid), Is.False);
        });

        await Server.WaitPost(() => SetMobState(MobState.Critical));
        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(MobState.Critical), "A critical wearer didn't turn the beacon to critical.");
            Assert.That(LightOn(beaconUid), Is.True, "The light wasn't on for a critical wearer.");
            AssertLightMatches(beaconUid, SEntMan.GetComponent<KsDistressBeaconComponent>(beaconUid).States[MobState.Critical]);
            Assert.That(SEntMan.GetComponent<ClothingComponent>(beaconUid).EquippedPrefix, Is.Not.Null, "The worn sprite wasn't lit.");
        });

        await Server.WaitPost(() => SetMobState(MobState.Alive));
        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(MobState.Alive), "A revived wearer didn't turn the beacon off.");
            Assert.That(LightOn(beaconUid), Is.False);
            Assert.That(SEntMan.GetComponent<ClothingComponent>(beaconUid).EquippedPrefix, Is.Null);
        });

        await Server.WaitPost(() => SetMobState(MobState.Dead));
        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(MobState.Dead), "A dead wearer didn't turn the beacon to dead.");
            Assert.That(LightOn(beaconUid), Is.True);
            AssertLightMatches(beaconUid, SEntMan.GetComponent<KsDistressBeaconComponent>(beaconUid).States[MobState.Dead]);
        });

        // The client's copy has the state, and so lights its icon.
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            var clientBeaconUid = ToClient(beaconUid);
            Assert.That(CEntMan.GetComponent<KsDistressBeaconComponent>(clientBeaconUid).State, Is.EqualTo(MobState.Dead));
        });

        await Server.WaitPost(() => SEntMan.System<InventorySystem>().TryUnequip(SPlayer, NeckSlot, force: true));
        await RunTicks(1); // the beeping marker goes at the end of the tick
        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(MobState.Alive), "Taking the beacon off didn't turn it off.");
            Assert.That(SEntMan.HasComponent<KsDistressBeaconBeepingComponent>(beaconUid), Is.False, "A beacon taken off was still looked at for beeping.");
            Assert.That(LightOn(beaconUid), Is.False);
        });
    }

    /// <summary>
    ///     Puts the player in a state with real damage, which keeps them there, unlike changing the state outright.
    /// </summary>
    private void SetDownState(MobState mobState)
    {
        var damageableSystem = SEntMan.System<DamageableSystem>();
        damageableSystem.ClearAllDamage(SPlayer);
        if (mobState == MobState.Alive)
            return;

        var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = SEntMan.System<MobThresholdSystem>().GetThresholdForState(SPlayer, mobState) } };
        damageableSystem.ChangeDamage(SPlayer, damage, ignoreResistances: true);
    }

    private bool HasOutline()
        => CEntMan.System<SpriteSystem>().HasPostShader((CPlayer, CEntMan.GetComponent<SpriteComponent>(CPlayer)), KsPostShaderIds.DistressBeaconOutline);

    /// <summary>
    ///     While the beacon is on, its wearer is outlined, with an opacity that is the light's pulse: fully opaque at the
    ///         peak and the settings' minimum at the trough. It goes when the beacon turns off, comes off, or the wearer
    ///         is revived.
    /// </summary>
    [Test]
    public async Task OutlineFollowsThePulseAndComesOffWithTheBeacon()
    {
        EntityUid beaconUid = default;
        await Server.WaitPost(() => beaconUid = SpawnAndWear());
        await RunTicks(5);
        await Client.WaitAssertion(() => Assert.That(HasOutline(), Is.False, "a beacon that is off outlined its wearer"));

        await Server.WaitPost(() => SetDownState(MobState.Critical));
        await RunTicks(5); // for the state to reach the client
        await Server.WaitAssertion(() => Assert.That(State(beaconUid), Is.EqualTo(MobState.Critical)));
        await Client.WaitAssertion(() => Assert.That(CEntMan.GetComponent<KsDistressBeaconComponent>(ToClient(beaconUid)).State, Is.EqualTo(MobState.Critical), "client state"));

        // Two whole pulses, whatever the rate.
        var pulseTicks = 0;
        await Server.WaitPost(() =>
        {
            var pulseRate = SEntMan.GetComponent<KsDistressBeaconComponent>(beaconUid).States[MobState.Critical].PulseRate;
            pulseTicks = (int)MathF.Ceiling(2f / pulseRate / TickPeriod);
        });

        var alphas = new List<float>();
        for (var i = 0; i < pulseTicks; i++)
        {
            await RunTicks(1);
            await Client.WaitAssertion(() =>
            {
                Assert.That(HasOutline(), Is.True, "a critical wearer wasn't outlined");

                var clientBeaconUid = ToClient(beaconUid);
                var beaconComponent = CEntMan.GetComponent<KsDistressBeaconComponent>(clientBeaconUid);
                var visualsComponent = CEntMan.GetComponent<KsDistressBeaconVisualsComponent>(clientBeaconUid);
                var settings = beaconComponent.States[MobState.Critical];

                // The light and the outline come from the one pulse.
                Assert.That(visualsComponent.OutlineAlpha, Is.EqualTo(KsDistressBeaconVisualsSystem.OutlineAlphaFor(settings, visualsComponent.Wave)).Within(0.0001f));
                Assert.That(CEntMan.GetComponent<PointLightComponent>(clientBeaconUid).Energy,
                    Is.EqualTo(KsDistressBeaconVisualsSystem.LightEnergyFor(settings, visualsComponent.Wave)).Within(0.0001f));
                Assert.That(visualsComponent.OutlineAlpha, Is.InRange(settings.OutlineMinAlpha - 0.0001f, 1.0001f));
                alphas.Add(visualsComponent.OutlineAlpha);
            });
        }

        await Client.WaitAssertion(() =>
        {
            var minAlpha = CEntMan.GetComponent<KsDistressBeaconComponent>(ToClient(beaconUid)).States[MobState.Critical].OutlineMinAlpha;
            Assert.That(alphas.Min(), Is.LessThan(minAlpha + 0.1f), "the outline never got down to its minimum");
            Assert.That(alphas.Max(), Is.GreaterThan(0.9f), "the outline never got fully opaque");
        });

        // The pulse itself, at its extremes.
        var criticalSettings = new KsDistressBeaconStateSettings { OutlineMinAlpha = 0.25f, PulseRate = 1f };
        Assert.That(KsDistressBeaconVisualsSystem.PulseWave(0f, criticalSettings.PulseRate), Is.EqualTo(0f).Within(0.0001f));
        Assert.That(KsDistressBeaconVisualsSystem.OutlineAlphaFor(criticalSettings, 0f), Is.EqualTo(0.25f));
        Assert.That(KsDistressBeaconVisualsSystem.OutlineAlphaFor(criticalSettings, 1f), Is.EqualTo(1f));
        Assert.That(KsDistressBeaconVisualsSystem.PulseWave(0.5f, criticalSettings.PulseRate), Is.EqualTo(1f).Within(0.0001f));

        // It stacks with another post-shader and sits after it, whichever was there first.
        await Client.WaitPost(() =>
        {
            var spriteSystem = CEntMan.System<SpriteSystem>();
            var spriteComponent = CEntMan.GetComponent<SpriteComponent>(CPlayer);
            var otherShader = Client.ResolveDependency<IPrototypeManager>().Index(_dissolveShader).InstanceUnique();
            spriteSystem.SetPostShader((CPlayer, spriteComponent), new SpriteComponent.PostShaderArgs(KsPostShaderIds.Dissolve, otherShader)
            {
                Before = KsPostShaderIds.BeforeOutlines,
            });

            var ids = spriteSystem.GetPostShaders((CPlayer, spriteComponent)).Select(entry => entry.Id).ToList();
            Assert.That(ids, Does.Contain(KsPostShaderIds.Dissolve));
            Assert.That(ids.IndexOf(KsPostShaderIds.DistressBeaconOutline), Is.GreaterThan(ids.IndexOf(KsPostShaderIds.Dissolve)),
                "the outline should be drawn over the dissolve");
            spriteSystem.RemovePostShader((CPlayer, spriteComponent), KsPostShaderIds.Dissolve);
        });

        // Revived: the beacon turns off, and so does the outline.
        await Server.WaitPost(() => SetDownState(MobState.Alive));
        await RunTicks(5);
        await Client.WaitAssertion(() => Assert.That(HasOutline(), Is.False, "a revived wearer kept the outline"));

        await Server.WaitPost(() => SetDownState(MobState.Dead));
        await RunTicks(5);
        await Client.WaitAssertion(() => Assert.That(HasOutline(), Is.True, "a dead wearer wasn't outlined"));

        await Server.WaitPost(() => SEntMan.System<InventorySystem>().TryUnequip(SPlayer, NeckSlot, force: true));
        await RunTicks(5);
        await Client.WaitAssertion(() => Assert.That(HasOutline(), Is.False, "taking the beacon off left the outline"));
    }

    [Test]
    public async Task BeaconPutOnSomeoneAlreadyDownIsOn()
    {
        await Server.WaitPost(() => SetMobState(MobState.Critical));

        EntityUid beaconUid = default;
        await Server.WaitPost(() => beaconUid = SpawnAndWear());

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(MobState.Critical), "A beacon put on a critical wearer stayed off.");
        });
    }

    [Test]
    public async Task BeaconKeepsBeepingWhileDown()
    {
        EntityUid beaconUid = default;
        await Server.WaitPost(() => beaconUid = SpawnAndWear());
        await Server.WaitPost(() => SetMobState(MobState.Critical));
        await RunTicks(1); // the player is healthy, so any longer and the mob state would right itself

        // It beeped as soon as its wearer went down, and booked the next one a critical interval on.
        await Server.WaitAssertion(() =>
        {
            var beaconComponent = SEntMan.GetComponent<KsDistressBeaconComponent>(beaconUid);
            var beepingComponent = SEntMan.GetComponent<KsDistressBeaconBeepingComponent>(beaconUid);
            var curTime = Server.Timing.CurTime;
            Assert.That(State(beaconUid), Is.EqualTo(MobState.Critical));
            Assert.That(beepingComponent.NextBeep, Is.GreaterThan(curTime), "The beacon never beeped.");
            Assert.That(beepingComponent.NextBeep, Is.LessThanOrEqualTo(curTime + beaconComponent.States[MobState.Critical].BeepInterval));
        });
    }

    [Test]
    public async Task GivingABeaconFillsAnEmptyNeckOnly()
    {
        var beaconSystem = SEntMan.System<KsFieldCommanderBeaconSystem>();
        var inventorySystem = SEntMan.System<InventorySystem>();

        await Server.WaitAssertion(() =>
        {
            Assert.That(beaconSystem.GiveBeacon(SPlayer), Is.True, "No beacon went on an empty neck.");
            Assert.That(inventorySystem.TryGetSlotEntity(SPlayer, NeckSlot, out var neckUid), Is.True);
            Assert.That(SEntMan.HasComponent<KsDistressBeaconComponent>(neckUid), Is.True);

            Assert.That(beaconSystem.GiveBeacon(SPlayer), Is.False, "A second beacon went on an occupied neck.");
        });
    }
}
