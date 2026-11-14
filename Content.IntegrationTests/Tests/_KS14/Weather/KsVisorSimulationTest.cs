using System.Linq;
using System.Numerics;
using Content.Client._KS14.Visor;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._KS14.Weather;

/// <summary>
///     The water on the visor: drops land while out in the weather and never past the cap, heavy ones run down leaving a
///         streak and pick up what they hit, flakes melt into water, and everything dries off under cover.
/// </summary>
[TestFixture]
[TestOf(typeof(KsVisorSimulation))]
public sealed class KsVisorSimulationTest
{
    private const float FrameTime = 1f / 60f;
    private const float Aspect = 16f / 9f;

    private static readonly KsVisorExposure Rain = new(DropsPerSecond: 20f, FlakesPerSecond: 0f, Mist: 0.6f, DropScale: 1f, Sheltered: false);

    private static KsVisorSimulation NewSimulation()
    {
        var random = new RobustRandom();
        random.SetSeed(1234);
        return new KsVisorSimulation(random);
    }

    private static void Run(KsVisorSimulation simulation, KsVisorExposure exposure, float seconds)
    {
        for (var elapsed = 0f; elapsed < seconds; elapsed += FrameTime)
            simulation.Update(FrameTime, exposure, Aspect);
    }

    [Test]
    public void RainLandsUpToTheCapAndMistBuildsUp()
    {
        var simulation = NewSimulation();
        Run(simulation, Rain, 1f);
        Assert.That(simulation.Drops, Is.Not.Empty, "rain should land on the visor");

        Run(simulation, Rain, 20f);
        Assert.That(simulation.Drops, Has.Count.LessThanOrEqualTo(KsVisorSimulation.MaxDrops), "never more drops than the shader takes");
        Assert.That(simulation.Mist, Is.EqualTo(Rain.Mist).Within(0.001f), "the mist builds up to the weather's");
    }

    [Test]
    public void EverythingDriesOffUnderCover()
    {
        var simulation = NewSimulation();
        Run(simulation, Rain, 5f);
        var sheltered = Rain with { Sheltered = true };

        Run(simulation, sheltered, 0.5f);
        var countUnderCover = simulation.Drops.Count;
        Run(simulation, sheltered, 0.5f);
        Assert.That(simulation.Drops.Count, Is.LessThanOrEqualTo(countUnderCover), "nothing new lands under cover");

        Run(simulation, sheltered, 60f);
        Assert.That(simulation.IsDry, Is.True, "under cover the visor dries off completely");
    }

    [Test]
    public void HeavyDropsRunDownAndPickUpWhatTheyHit()
    {
        var simulation = NewSimulation();
        simulation.Drops.Add(new KsVisorDrop { Position = new Vector2(0.5f, 0.8f), Radius = 0.03f, Life = 60f, Melt = 1f });
        simulation.Drops.Add(new KsVisorDrop { Position = new Vector2(0.5f, 0.6f), Radius = 0.01f, Life = 60f, SlideDelay = 100f, Melt = 1f });
        simulation.Drops.Add(new KsVisorDrop { Position = new Vector2(0.1f, 0.5f), Radius = 0.006f, Life = 60f, Melt = 1f });

        Run(simulation, KsVisorExposure.None, 8f);

        var runner = simulation.Drops.OrderByDescending(drop => drop.Radius).First();
        Assert.That(runner.Position.Y, Is.LessThan(0.6f), "a heavy drop should have run down");
        Assert.That(runner.Trail, Is.GreaterThan(0f), "it leaves a streak above it");
        Assert.That(simulation.Drops, Has.Count.EqualTo(2), "it picked up the drop in its way");
        Assert.That(simulation.Drops.Any(drop => drop.Position.X < 0.2f && MathF.Abs(drop.Position.Y - 0.5f) < 0.001f), Is.True,
            "a small drop clings where it landed");
    }

    [Test]
    public void FlakesMeltIntoWater()
    {
        var simulation = NewSimulation();
        simulation.Drops.Add(new KsVisorDrop { Position = new Vector2(0.5f, 0.5f), Radius = 0.015f, Life = 60f, Melt = 0f });

        Run(simulation, KsVisorExposure.None, 5f);
        Assert.That(simulation.Drops[0].Melt, Is.EqualTo(1f), "a wet snowflake melts within a few seconds");
    }

    /// <summary>
    ///     Two values from 0 to 1 share a float on their way to the shader. Both have to come back as they went in,
    ///         including the ends, which are where a value most easily spills into its neighbour's part.
    /// </summary>
    [Test]
    public void PackedValuesComeBackIntact()
    {
        var values = new[] { 0f, 0.0005f, 0.25f, 0.5f, 0.731f, 0.999f, 1f };
        foreach (var whole in values)
        {
            foreach (var fraction in values)
            {
                var (unpackedWhole, unpackedFraction) = KsVisorSimulation.Unpack(KsVisorSimulation.Pack(whole, fraction));
                Assert.That(unpackedWhole, Is.EqualTo(whole).Within(0.5f / 1023f), $"the first of ({whole}, {fraction})");
                Assert.That(unpackedFraction, Is.EqualTo(fraction).Within(0.001f), $"the second of ({whole}, {fraction})");
            }
        }
    }
}
