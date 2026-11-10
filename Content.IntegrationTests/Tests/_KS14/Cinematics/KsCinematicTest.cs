using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Cinematics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio.Systems;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;
using ClientCinematicSystem = Content.Client._KS14.Cinematics.KsCinematicSystem;
using ServerCinematicSystem = Content.Server._KS14.Cinematics.KsCinematicSystem;

namespace Content.IntegrationTests.Tests._KS14.Cinematics;

[TestFixture]
[TestOf(typeof(ServerCinematicSystem))]
public sealed class KsCinematicTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: ksCinematic
  id: KsTestCinematic
  sprite: _KS14/Cinematic/station_explosion.rsi
  scenes:
  - state: intro_nuke
    duration: 1
  - state: station_intact
    duration: 2
    stretch: true
  - state: summary_nukewin
    duration: 3
";

    // Deliberately NOT static: it only exists in the test YAML above, and the YAML linter validates static ids.
    private readonly ProtoId<KsCinematicPrototype> _testCinematic = "KsTestCinematic";

    /// <summary>
    ///     The stretched scene ends when the caller asks, but never before its own duration is up.
    /// </summary>
    [Test]
    public async Task StretchedSceneEndsWhenAsked()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var cinematicSystem = server.EntMan.System<ServerCinematicSystem>();
            var cinematic = SProtoMan.Index(_testCinematic);
            var stretchedSceneMinimumEnd = cinematic.Scenes[0].Duration + cinematic.Scenes[1].Duration;
            var lateEnd = stretchedSceneMinimumEnd + TimeSpan.FromSeconds(5);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cinematicSystem.Play(_testCinematic, Filter.Empty(), stretchUntil: lateEnd),
                    Is.EqualTo(lateEnd), "The stretched scene didn't stretch.");
                Assert.That(cinematicSystem.Play(_testCinematic, Filter.Empty(), stretchUntil: TimeSpan.Zero),
                    Is.EqualTo(stretchedSceneMinimumEnd), "The stretched scene was cut short.");
            }
        });
    }

    /// <summary>
    ///     A cinematic the server plays starts on the client.
    /// </summary>
    [Test]
    public async Task ClientPlaysServerCinematic()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var clientCinematicSystem = client.EntMan.System<ClientCinematicSystem>();

        Assert.That(clientCinematicSystem.Active, Is.False);

        await server.WaitPost(() =>
            server.EntMan.System<ServerCinematicSystem>().Play(_testCinematic, Filter.Broadcast()));
        await Pair.RunTicksSync(5);

        Assert.That(clientCinematicSystem.Active, Is.True, "The client didn't start the cinematic.");
    }

    /// <summary>
    ///     Every scene of every cinematic names a state its RSI has, and is shown for some time.
    /// </summary>
    [Test]
    public async Task EveryCinematicHasItsStates()
    {
        var client = Pair.Client;
        var resourceCache = client.ResolveDependency<IResourceCache>();
        var resourceManager = client.ResolveDependency<IResourceManager>();
        var audioSystem = client.EntMan.System<SharedAudioSystem>();

        await client.WaitAssertion(() =>
        {
            foreach (var cinematic in CProtoMan.EnumeratePrototypes<KsCinematicPrototype>())
            {
                var rsiPath = SpriteSpecifierSerializer.TextureRoot / cinematic.Sprite;
                Assert.That(resourceCache.TryGetResource<RSIResource>(rsiPath, out var resource),
                    $"{cinematic.ID}'s sprite {rsiPath} doesn't load.");

                Assert.That(cinematic.Scenes, Is.Not.Empty, $"{cinematic.ID} has no scenes.");
                foreach (var scene in cinematic.Scenes)
                {
                    Assert.That(resource!.RSI.TryGetState(scene.State, out _),
                        $"{cinematic.ID}'s state {scene.State} isn't in {rsiPath}.");
                    Assert.That(scene.Duration, Is.GreaterThan(TimeSpan.Zero),
                        $"{cinematic.ID}'s scene {scene.State} is never shown.");

                    foreach (var sceneSound in scene.Sounds)
                    {
                        var soundPath = audioSystem.GetAudioPath(audioSystem.ResolveSound(sceneSound.Sound));
                        Assert.That(soundPath != null && resourceManager.ContentFileExists(new ResPath(soundPath)),
                            $"{cinematic.ID}'s scene {scene.State} plays {soundPath}, which doesn't exist.");
                    }
                }
            }
        });
    }
}
