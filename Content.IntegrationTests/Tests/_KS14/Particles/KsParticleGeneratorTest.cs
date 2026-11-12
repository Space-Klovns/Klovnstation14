#nullable enable
using System.IO;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Particles;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using YamlDotNet.RepresentationModel;

namespace Content.IntegrationTests.Tests._KS14.Particles;

/// <summary>
///     The generator's maths needs no game: a seeded random and the struct.
/// </summary>
[TestFixture]
[TestOf(typeof(KsParticleGenerator))]
public sealed class KsParticleGeneratorMathTest
{
    private const int Samples = 2000;

    private static System.Random Seeded() => new(1234);

    [Test]
    public void ConstantAlwaysGivesItsValue()
    {
        var generator = KsParticleGenerator.Constant(new Vector3(1f, 2f, 3f));
        var random = Seeded();
        for (var i = 0; i < 10; i++)
            Assert.That(generator.SampleVector3(random), Is.EqualTo(new Vector3(1f, 2f, 3f)));
    }

    [Test]
    public void ScalarConstantBroadcastsToEveryComponent()
    {
        var generator = KsParticleGenerator.Constant(0.5f);
        Assert.That(generator.SampleVector2(Seeded()), Is.EqualTo(new Vector2(0.5f, 0.5f)));
    }

    [TestCase(KsParticleGeneratorKind.Num)]
    [TestCase(KsParticleGeneratorKind.Vector)]
    [TestCase(KsParticleGeneratorKind.Box)]
    public void LinearKindsStayBetweenTheirOperands(KsParticleGeneratorKind kind)
    {
        var generator = new KsParticleGenerator(kind, new Vector4(-1f, 2f, 0f, 0f), new Vector4(1f, 4f, 0f, 0f));
        var random = Seeded();
        for (var i = 0; i < Samples; i++)
        {
            var value = generator.SampleVector3(random);
            Assert.That(value.X, Is.InRange(-1f, 1f));
            Assert.That(value.Y, Is.InRange(2f, 4f));
            Assert.That(value.Z, Is.EqualTo(0f));
        }
    }

    /// <summary>
    ///     A vector generator walks the line between its operands: one fraction for every component. A box draws
    ///         each component on its own, so its points leave the line.
    /// </summary>
    [Test]
    public void VectorStaysOnTheLineAndBoxDoesNot()
    {
        var a = new Vector4(0f, 0f, 0f, 0f);
        var b = new Vector4(1f, 2f, 0f, 0f);
        var vector = new KsParticleGenerator(KsParticleGeneratorKind.Vector, a, b);
        var box = new KsParticleGenerator(KsParticleGeneratorKind.Box, a, b);
        var random = Seeded();

        var offLine = 0;
        for (var i = 0; i < Samples; i++)
        {
            var onLine = vector.SampleVector2(random);
            Assert.That(onLine.Y, Is.EqualTo(onLine.X * 2f).Within(1e-4f), "a vector sample left the line from a to b");

            var inBox = box.SampleVector2(random);
            if (MathF.Abs(inBox.Y - inBox.X * 2f) > 0.05f)
                offLine++;
        }

        Assert.That(offLine, Is.GreaterThan(Samples / 2), "box samples should mostly be off the line");
    }

    [TestCase(KsParticleGeneratorKind.Circle, false)]
    [TestCase(KsParticleGeneratorKind.Sphere, true)]
    [TestCase(KsParticleGeneratorKind.Square, false)]
    [TestCase(KsParticleGeneratorKind.Cube, true)]
    public void RadialKindsStayWithinTheirRadii(KsParticleGeneratorKind kind, bool threeDimensional)
    {
        var generator = new KsParticleGenerator(kind, new Vector4(0.5f), new Vector4(2f));
        var random = Seeded();
        var sawZ = false;
        for (var i = 0; i < Samples; i++)
        {
            var value = generator.SampleVector3(random);
            var radius = kind is KsParticleGeneratorKind.Square or KsParticleGeneratorKind.Cube
                ? MathF.Max(MathF.Abs(value.X), MathF.Max(MathF.Abs(value.Y), MathF.Abs(value.Z)))
                : value.Length();
            Assert.That(radius, Is.InRange(0.5f - 1e-4f, 2f + 1e-4f), $"{kind} sample {value} is outside the ring");
            sawZ |= value.Z != 0f;
        }

        Assert.That(sawZ, Is.EqualTo(threeDimensional), $"{kind} should {(threeDimensional ? "" : "not ")}use z");
    }

    /// <summary>
    ///     Each distribution leans the way BYOND's does: linear and square towards B, normal towards the middle.
    /// </summary>
    [Test]
    public void DistributionsLeanTheRightWay()
    {
        var random = Seeded();
        float Mean(KsParticleDistribution distribution)
        {
            var sum = 0f;
            for (var i = 0; i < Samples; i++)
                sum += KsParticleGenerator.Fraction(random, distribution);
            return sum / Samples;
        }

        var uniform = Mean(KsParticleDistribution.Uniform);
        var normal = Mean(KsParticleDistribution.Normal);
        var linear = Mean(KsParticleDistribution.Linear);
        var square = Mean(KsParticleDistribution.Square);

        Assert.Multiple(() =>
        {
            Assert.That(uniform, Is.EqualTo(0.5f).Within(0.05f));
            Assert.That(normal, Is.EqualTo(0.5f).Within(0.05f));
            Assert.That(linear, Is.EqualTo(2f / 3f).Within(0.05f), "sqrt(U) has mean 2/3");
            Assert.That(square, Is.EqualTo(0.75f).Within(0.05f), "cbrt(U) has mean 3/4");
        });

        var normalSpread = 0f;
        var uniformSpread = 0f;
        for (var i = 0; i < Samples; i++)
        {
            normalSpread += MathF.Abs(KsParticleGenerator.Fraction(random, KsParticleDistribution.Normal) - 0.5f);
            uniformSpread += MathF.Abs(KsParticleGenerator.Fraction(random, KsParticleDistribution.Uniform) - 0.5f);
        }

        Assert.That(normalSpread, Is.LessThan(uniformSpread), "normal samples should sit closer to the middle than uniform ones");
    }

    [Test]
    public void ColorConstantRoundTrips()
    {
        var generator = KsParticleGenerator.Constant(new Color(0.2f, 0.4f, 0.6f, 0.8f));
        Assert.That(generator.IsColor);
        Assert.That(generator.SampleColor(Seeded()), Is.EqualTo(new Color(0.2f, 0.4f, 0.6f, 0.8f)));
    }

    [Test]
    public void GradientBlendsClampsAndLoops()
    {
        var gradient = new KsParticleGradient
        {
            Colors = new() { Color.Red, Color.Lime, Color.Blue },
        };

        Assert.Multiple(() =>
        {
            Assert.That(gradient.Evaluate(0f), Is.EqualTo(Color.Red));
            Assert.That(gradient.Evaluate(1f), Is.EqualTo(Color.Blue));
            Assert.That(gradient.Evaluate(5f), Is.EqualTo(Color.Blue), "past the end holds the last colour");
            Assert.That(gradient.Evaluate(-5f), Is.EqualTo(Color.Red), "before the start holds the first");

            var middle = gradient.Evaluate(0.25f);
            Assert.That(middle.R, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(middle.G, Is.EqualTo(0.5f).Within(0.01f));
        });

        gradient.Loop = true;
        Assert.That(gradient.Evaluate(1.25f).R, Is.EqualTo(0.5f).Within(0.01f), "looped, 1.25 is 0.25 again");

        gradient.Loop = false;
        gradient.Positions = new() { 0f, 3f, 6f };
        Assert.That(gradient.Evaluate(1.5f).R, Is.EqualTo(0.5f).Within(0.01f), "explicit positions stretch the segments");
    }

    [Test]
    public void HslGradientTakesTheShortWayRoundTheHueCircle()
    {
        var gradient = new KsParticleGradient
        {
            Colors = new() { Color.FromHex("#ff0000"), Color.FromHex("#ff00ff") }, // hue 0 and hue 5/6
            Space = KsColorSpace.Hsl,
        };

        var middle = Color.ToHsl(gradient.Evaluate(0.5f));
        Assert.That(middle.X, Is.EqualTo(11f / 12f).Within(0.01f), "halfway between hue 0 and hue 5/6, the short way, is 11/12");
    }
}

/// <summary>
///     The serializer behind every generator field: the three YAML shapes, and that the linter's validation agrees
///         with the reader.
/// </summary>
[TestFixture]
[TestOf(typeof(KsParticleGeneratorSerializer))]
public sealed class KsParticleGeneratorSerializerTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    private static readonly ProtoId<KsParticlesPrototype> SmokePrototype = "KsSmoke";
    private static readonly ProtoId<KsParticlesPrototype> SteamPrototype = "KsSteam";
    private static readonly ProtoId<KsParticlesPrototype> EmbersPrototype = "KsEmbers";
    private static readonly ProtoId<KsParticlesPrototype> SparksPrototype = "KsSparks";

    private ISerializationManager SerializationManager => Server.ResolveDependency<ISerializationManager>();

    private static DataNode Parse(string yaml)
    {
        var yamlStream = new YamlStream();
        yamlStream.Load(new StringReader(yaml));
        return yamlStream.Documents[0].RootNode.ToDataNode();
    }

    // Reading a plain value needs no entity, so the test thread may do it, as the YAML linter does in parallel.
    private KsParticleGenerator Read(string yaml) => SerializationManager.Read<KsParticleGenerator>(Parse(yaml));

    private bool IsValid(string yaml) => SerializationManager.ValidateNode<KsParticleGenerator>(Parse(yaml)).Valid;

    [Test]
    public void ScalarIsAConstant()
    {
        var generator = Read("1.5");
        Assert.Multiple(() =>
        {
            Assert.That(generator.Kind, Is.EqualTo(KsParticleGeneratorKind.Constant));
            Assert.That(generator.A, Is.EqualTo(new Vector4(1.5f)));
            Assert.That(generator.Components, Is.EqualTo(1));
        });
    }

    [Test]
    public void ListIsAConstantVectorWithZDefaultingToZero()
    {
        var two = Read("[ 0, 0.5 ]");
        var three = Read("[ 1, 2, 3 ]");
        Assert.Multiple(() =>
        {
            Assert.That(two.A, Is.EqualTo(new Vector4(0f, 0.5f, 0f, 0f)));
            Assert.That(two.Components, Is.EqualTo(2));
            Assert.That(three.A, Is.EqualTo(new Vector4(1f, 2f, 3f, 0f)));
            Assert.That(three.CanProduceZ, Is.True);
            Assert.That(two.CanProduceZ, Is.False);
        });
    }

    [Test]
    public void ColourIsAConstantColour()
    {
        var hex = Read("\"#ff000080\"");
        var named = Read("red");
        Assert.Multiple(() =>
        {
            Assert.That(hex.IsColor);
            Assert.That(hex.SampleColor(new System.Random(1)), Is.EqualTo(Color.FromHex("#ff000080")));
            Assert.That(named.IsColor);
            Assert.That(named.SampleColor(new System.Random(1)), Is.EqualTo(Color.Red));
        });
    }

    [Test]
    public void MappingIsAGenerator()
    {
        var generator = Read("{ kind: circle, a: 0, b: 0.5, dist: normal }");
        Assert.Multiple(() =>
        {
            Assert.That(generator.Kind, Is.EqualTo(KsParticleGeneratorKind.Circle));
            Assert.That(generator.A.X, Is.EqualTo(0f));
            Assert.That(generator.B.X, Is.EqualTo(0.5f));
            Assert.That(generator.Distribution, Is.EqualTo(KsParticleDistribution.Normal));
        });

        var box = Read("{ kind: box, a: [ -0.4, -0.5 ], b: [ 0.4, 0.5 ] }");
        Assert.That(box.A, Is.EqualTo(new Vector4(-0.4f, -0.5f, 0f, 0f)));
        Assert.That(box.B, Is.EqualTo(new Vector4(0.4f, 0.5f, 0f, 0f)));
        Assert.That(box.Distribution, Is.EqualTo(KsParticleDistribution.Uniform));

        var colour = Read("{ kind: color, a: \"#ff0000\", b: \"#0000ff\" }");
        Assert.That(colour.Kind, Is.EqualTo(KsParticleGeneratorKind.Vector));
        Assert.That(colour.IsColor);

        var missingB = Read("{ kind: num, a: 3 }");
        Assert.That(missingB.B, Is.EqualTo(missingB.A), "a missing b equals a");
    }

    [Test]
    public void WritingReadsBackTheSame()
    {
        foreach (var yaml in new[] { "1.5", "[ 0, 0.5 ]", "\"#ff000080\"", "{ kind: sphere, a: 0, b: 2, dist: square }", "{ kind: box, a: [ -1, -2, -3 ], b: [ 1, 2, 3 ] }" })
        {
            var generator = Read(yaml);
            var again = SerializationManager.Read<KsParticleGenerator>(SerializationManager.WriteValue(generator));

            Assert.Multiple(() =>
            {
                Assert.That(again.Kind, Is.EqualTo(generator.Kind), yaml);
                Assert.That(again.A, Is.EqualTo(generator.A), yaml);
                Assert.That(again.B, Is.EqualTo(generator.B), yaml);
                Assert.That(again.Distribution, Is.EqualTo(generator.Distribution), yaml);
                Assert.That(again.IsColor, Is.EqualTo(generator.IsColor), yaml);
            });
        }
    }

    /// <summary>
    ///     What the YAML linter sees: the validator agrees with the reader on what is well formed.
    /// </summary>
    [Test]
    public void BadInputIsRefusedAndGoodInputIsNot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IsValid("1.5"), "a scalar");
            Assert.That(IsValid("[ 0, 0.5 ]"), "a vector");
            Assert.That(IsValid("red"), "a named colour");
            Assert.That(IsValid("{ kind: circle, a: 0, b: 1, dist: normal }"), "a generator");

            Assert.That(IsValid("notanumber"), Is.False, "a word that is not a colour");
            Assert.That(IsValid("[ 1 ]"), Is.False, "a one-element list");
            Assert.That(IsValid("{ a: 1, b: 2 }"), Is.False, "no kind");
            Assert.That(IsValid("{ kind: circle, a: 0, b: 1, dist: bell }"), Is.False, "an unknown distribution");
            Assert.That(IsValid("{ kind: circle, radius: 1 }"), Is.False, "an unknown field");

            Assert.Throws<InvalidMappingException>(() => Read("notanumber"), "reading refuses what validation refuses");
        });
    }

    /// <summary>
    ///     Every shipped prototype reads, and the generators it declares are the kinds its YAML names.
    /// </summary>
    [Test]
    public void ShippedPrototypesLoad()
    {
        var prototypes = Server.ProtoMan.EnumeratePrototypes<KsParticlesPrototype>().Where(prototype => !prototype.Abstract).ToList();
        Assert.That(prototypes.Select(prototype => prototype.ID), Does.Contain(SmokePrototype.Id).And.Contain(EmbersPrototype.Id).And.Contain(SparksPrototype.Id));

        var smoke = Server.ProtoMan.Index(SmokePrototype);
        var steam = Server.ProtoMan.Index(SteamPrototype);
        Assert.Multiple(() =>
        {
            Assert.That(smoke.Drift.Kind, Is.EqualTo(KsParticleGeneratorKind.Sphere));
            Assert.That(smoke.Sprites, Has.Count.EqualTo(3));
            Assert.That(steam.Sprites, Has.Count.EqualTo(3), "!Clear replaces the parent's sprites rather than appending");
            Assert.That(steam.Gravity, Is.EqualTo(smoke.Gravity), "a child inherits what it does not restate");
        });
    }
}
