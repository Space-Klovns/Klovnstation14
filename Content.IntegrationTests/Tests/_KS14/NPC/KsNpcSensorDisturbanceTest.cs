#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Systems;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     A disturbance nothing can hear is no disturbance, and not an error: a gun with no gunshot sound disturbs over that
///         sound's range, which is none, and the lookup behind it asserts on a range that is not positive.
/// </summary>
[TestFixture]
[TestOf(typeof(NpcSensorSystem))]
public sealed class KsNpcSensorDisturbanceTest : GameTest
{
    [TestCase(0f)]
    [TestCase(-1f)]
    public async Task DisturbanceOverNoRangeIsNothing(float radius)
    {
        var testMap = await Pair.CreateTestMap();

        await Pair.Server.WaitAssertion(() =>
        {
            var sensorSystem = Pair.Server.EntMan.System<NpcSensorSystem>();
            Assert.DoesNotThrow(() => sensorSystem.DoDisturbance(new EntityCoordinates(testMap.MapUid, 0f, 0f), radius),
                "a disturbance over no range threw, as it does for every shot of a gun with no gunshot sound");
        });
    }
}
