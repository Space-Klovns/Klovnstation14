#nullable enable
using System;
using System.Linq;
using Content.Client._KS14.FieldCommander;
using Content.Client._KS14.Graphics;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     A field commander defender flashes with static as they go critical and as they die, in the colour and for the
///         time their marker says, and anyone else does not. The flash stacks with other post-shaders.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderDownFlashSystem))]
public sealed class KsFieldCommanderDownFlashTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _defender = "KsTestDownFlashDefender";
    private readonly EntProtoId _bystander = "KsTestDownFlashBystander";

    // Not static: shaders are a client-only kind, which the YAML linter does not know.
    private readonly ProtoId<ShaderPrototype> _dissolveShader = "KsDissolve";

    private static readonly Color CriticalColor = Color.FromHex("#12AB34CC");
    private static readonly Color DeadColor = Color.FromHex("#000000EE");

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestDownFlashBystander
  components:
  - type: Sprite
  - type: MobState

- type: entity
  parent: KsTestDownFlashBystander
  id: KsTestDownFlashDefender
  components:
  - type: KsFieldCommanderDefender
    flashes:
      Critical:
        color: '#12AB34CC'
        duration: 0.4
      Dead:
        color: '#000000EE'
        duration: 0.3
";

    private bool Flashing(EntityUid clientUid)
        => Pair.Client.System<SpriteSystem>().HasPostShader((clientUid, Pair.Client.EntMan.GetComponent<SpriteComponent>(clientUid)), KsPostShaderIds.DownFlash);

    private int TicksFor(float seconds)
        => (int)Math.Ceiling(seconds * Pair.Server.Timing.TickRate) + 3;

    [Test]
    public async Task DefenderFlashesAsTheyGoDown()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();
        EntityUid defenderUid = default;
        EntityUid bystanderUid = default;

        await server.WaitPost(() =>
        {
            defenderUid = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            bystanderUid = server.EntMan.SpawnAtPosition(_bystander, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
        });
        await Pair.RunTicksSync(10);

        var clientDefender = Pair.ToClientUid(defenderUid);
        var clientBystander = Pair.ToClientUid(bystanderUid);

        await client.WaitAssertion(() =>
        {
            Assert.That(Flashing(clientDefender), Is.False, "a healthy defender flashed");
            Assert.That(Flashing(clientBystander), Is.False);
        });

        // Critical: the first colour, for the first time.
        await server.WaitPost(() =>
        {
            server.System<MobStateSystem>().ChangeMobState(defenderUid, MobState.Critical);
            server.System<MobStateSystem>().ChangeMobState(bystanderUid, MobState.Critical);
        });
        await Pair.RunTicksSync(3);

        await client.WaitAssertion(() =>
        {
            Assert.That(Flashing(clientDefender), Is.True, "a defender going critical should flash");
            Assert.That(Flashing(clientBystander), Is.False, "someone who is no defender flashed");

            var visualsComponent = client.EntMan.GetComponent<KsFieldCommanderDownFlashVisualsComponent>(clientDefender);
            Assert.That(visualsComponent.Color, Is.EqualTo(CriticalColor));
            Assert.That(visualsComponent.Duration, Is.EqualTo(TimeSpan.FromSeconds(0.4)));
        });

        // It is over after its duration.
        await Pair.RunTicksSync(TicksFor(0.4f));
        await client.WaitAssertion(() => Assert.That(Flashing(clientDefender), Is.False, "the flash should have faded away"));

        // Dead: the other colour, for its own time.
        await server.WaitPost(() => server.System<MobStateSystem>().ChangeMobState(defenderUid, MobState.Dead));
        await Pair.RunTicksSync(3);

        await client.WaitAssertion(() =>
        {
            Assert.That(Flashing(clientDefender), Is.True, "a defender dying should flash");
            var visualsComponent = client.EntMan.GetComponent<KsFieldCommanderDownFlashVisualsComponent>(clientDefender);
            Assert.That(visualsComponent.Color, Is.EqualTo(DeadColor));
            Assert.That(visualsComponent.Duration, Is.EqualTo(TimeSpan.FromSeconds(0.3)));
        });

        await Pair.RunTicksSync(TicksFor(0.3f));
        await client.WaitAssertion(() => Assert.That(Flashing(clientDefender), Is.False));

        // Revived: no flash for getting up.
        await server.WaitPost(() => server.System<MobStateSystem>().ChangeMobState(defenderUid, MobState.Alive));
        await Pair.RunTicksSync(3);
        await client.WaitAssertion(() => Assert.That(Flashing(clientDefender), Is.False, "getting up flashed"));

        // Revived while still flashing: getting up ends the flash there and then, well inside its own time.
        const int syncTicks = 3;
        var criticalDuration = TimeSpan.Zero;
        await server.WaitPost(() => server.System<MobStateSystem>().ChangeMobState(defenderUid, MobState.Critical));
        await Pair.RunTicksSync(syncTicks);
        await client.WaitAssertion(() =>
        {
            Assert.That(Flashing(clientDefender), Is.True, "a defender going critical again should flash");
            criticalDuration = client.EntMan.GetComponent<KsFieldCommanderDownFlashVisualsComponent>(clientDefender).Duration;
        });

        await server.WaitPost(() => server.System<MobStateSystem>().ChangeMobState(defenderUid, MobState.Alive));
        await Pair.RunTicksSync(syncTicks);
        Assert.That(server.Timing.TickPeriod * (syncTicks * 2 + 2), Is.LessThan(criticalDuration),
            "the flash must still have time to run, or its ending proves nothing");
        await client.WaitAssertion(() => Assert.That(Flashing(clientDefender), Is.False, "getting up should end a flash in progress"));
    }

    /// <summary>
    ///     The flash sits with the other post-shaders instead of replacing them, before the outlines, and takes
    ///         only itself away.
    /// </summary>
    [Test]
    public async Task FlashStacksWithOtherPostShaders()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();
        EntityUid defenderUid = default;

        await server.WaitPost(() => defenderUid = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f)));
        await Pair.RunTicksSync(10);
        var clientDefender = Pair.ToClientUid(defenderUid);

        await client.WaitPost(() =>
        {
            var spriteSystem = client.System<SpriteSystem>();
            var spriteComponent = client.EntMan.GetComponent<SpriteComponent>(clientDefender);
            var dissolveShader = client.ProtoMan.Index(_dissolveShader).InstanceUnique();
            var outlineShader = client.ProtoMan.Index(_dissolveShader).InstanceUnique();
            spriteSystem.SetPostShader((clientDefender, spriteComponent), new SpriteComponent.PostShaderArgs(KsPostShaderIds.Dissolve, dissolveShader)
            {
                Before = KsPostShaderIds.BeforeOutlines,
            });
            spriteSystem.SetPostShader((clientDefender, spriteComponent), new SpriteComponent.PostShaderArgs(KsPostShaderIds.DistressBeaconOutline, outlineShader)
            {
                After = KsPostShaderIds.AfterBaseEffects,
            });
        });

        await server.WaitPost(() => server.System<MobStateSystem>().ChangeMobState(defenderUid, MobState.Critical));
        await Pair.RunTicksSync(3);

        await client.WaitAssertion(() =>
        {
            var ids = client.System<SpriteSystem>().GetPostShaders((clientDefender, client.EntMan.GetComponent<SpriteComponent>(clientDefender)))
                .Select(entry => entry.Id)
                .ToList();
            Assert.That(ids, Is.SupersetOf(new[] { KsPostShaderIds.Dissolve, KsPostShaderIds.DownFlash, KsPostShaderIds.DistressBeaconOutline }),
                "the flash should sit beside the others");
            Assert.That(ids.IndexOf(KsPostShaderIds.DownFlash), Is.LessThan(ids.IndexOf(KsPostShaderIds.DistressBeaconOutline)), "the outline is drawn over the flash");
        });

        // It ends, and only it goes.
        await Pair.RunTicksSync(TicksFor(0.4f));
        await client.WaitAssertion(() =>
        {
            var ids = client.System<SpriteSystem>().GetPostShaders((clientDefender, client.EntMan.GetComponent<SpriteComponent>(clientDefender)))
                .Select(entry => entry.Id)
                .ToList();
            Assert.That(ids, Does.Not.Contain(KsPostShaderIds.DownFlash));
            Assert.That(ids, Is.SupersetOf(new[] { KsPostShaderIds.Dissolve, KsPostShaderIds.DistressBeaconOutline }));
        });
    }
}
