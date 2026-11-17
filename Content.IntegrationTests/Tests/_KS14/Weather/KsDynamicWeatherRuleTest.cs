#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Weather;
using Content.Server.GameTicking;
using Content.Server.Weather;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Weather;

/// <summary>
///     Dynamic weather puts its state's weathers on its map, all of them together, follows its transitions when the state
///         runs out, fades out what the next state doesn't have, and clears up when the rule ends.
/// </summary>
[TestFixture]
[TestOf(typeof(KsDynamicWeatherRuleSystem))]
public sealed class KsDynamicWeatherRuleTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _rule = "KsTestDynamicWeatherRule";
    private readonly EntProtoId _rain = "KsTestWeatherRain";
    private readonly EntProtoId _snow = "KsTestWeatherSnow";

    // Snowy lasts long, Sleet briefly, then always gives way to Snowy.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WeatherBase
  id: KsTestWeatherRain
  components:
  - type: WeatherStatusEffect
    sprite:
      sprite: /Textures/Effects/weather.rsi
      state: rain

- type: entity
  parent: WeatherBase
  id: KsTestWeatherSnow
  components:
  - type: WeatherStatusEffect
    sprite:
      sprite: /Textures/Effects/weather.rsi
      state: snowfall_light

- type: entity
  parent: BaseGameRule
  id: KsTestDynamicWeatherRule
  components:
  - type: KsDynamicWeatherRule
    initialState: Snowy
    states:
    - name: Snowy
      weathers:
      - KsTestWeatherSnow
      minDuration: 600
      maxDuration: 600
      next:
        Sleet: 1
    - name: Sleet
      weathers:
      - KsTestWeatherRain
      - KsTestWeatherSnow
      minDuration: 5
      maxDuration: 5
      next:
        Snowy: 1
";

    [Test]
    public async Task WeatherFollowsItsStates()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var weatherSystem = server.System<WeatherSystem>();
        var ruleSystem = server.System<KsDynamicWeatherRuleSystem>();
        EntityUid ruleUid = default;

        await server.WaitPost(() =>
        {
            Assert.That(server.System<GameTicker>().StartGameRule(_rule, out ruleUid), Is.True);
            ruleSystem.SetMap((ruleUid, server.EntMan.GetComponent<KsDynamicWeatherRuleComponent>(ruleUid)), testMap.MapUid);
        });
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(weatherSystem.HasWeather(testMap.MapId, _snow), Is.True, "the round starts in its initial state");
            Assert.That(weatherSystem.HasWeather(testMap.MapId, _rain), Is.False);
            Assert.That(ruleSystem.TrySetState((ruleUid, server.EntMan.GetComponent<KsDynamicWeatherRuleComponent>(ruleUid)), "Sleet"), Is.True);
            Assert.That(weatherSystem.HasWeather(testMap.MapId, _rain) && weatherSystem.HasWeather(testMap.MapId, _snow), Is.True,
                "a state's weathers all come together");
        });

        // Sleet runs out, and gives way to Snowy, which has no rain: the rain fades out, and is gone once it has.
        await RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsDynamicWeatherRuleComponent>(ruleUid);
            Assert.That(ruleComponent.States[ruleComponent.StateIndex].Name, Is.EqualTo("Snowy"), "a state gives way to what it names as next");
        });

        await RunSeconds((float)SharedWeatherSystemShutdownSeconds + 2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(weatherSystem.HasWeather(testMap.MapId, _rain), Is.False, "what the next state lacks fades out");
            Assert.That(weatherSystem.HasWeather(testMap.MapId, _snow), Is.True, "what it shares stays");
        });

        await server.WaitPost(() => server.System<GameTicker>().EndGameRule(ruleUid));
        await RunSeconds((float)SharedWeatherSystemShutdownSeconds + 2f);
        await server.WaitAssertion(() =>
            Assert.That(weatherSystem.HasWeather(testMap.MapId, _snow), Is.False, "the weather clears up when the rule ends"));
    }

    private static double SharedWeatherSystemShutdownSeconds => Content.Shared.Weather.SharedWeatherSystem.ShutdownTime.TotalSeconds;
}
