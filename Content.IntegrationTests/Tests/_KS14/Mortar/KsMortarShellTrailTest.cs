#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.SupplyPod;
using Content.Shared._KS14.Trail;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Mortar;

/// <summary>
///     Content: a mortar shell leaves the smoke trail of a supply pod at half its width, and supply pods keep theirs.
/// </summary>
[TestFixture]
[TestOf(typeof(KsTrailComponent))]
public sealed class KsMortarShellTrailTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly EntProtoId MortarShell = "KsMortarShell";
    private static readonly EntProtoId CargoPod = "SupplyPodCargoBase";
    private static readonly EntProtoId MortarShellTrail = "KsMortarShellTrail";

    [Test]
    public async Task AMortarShellTrailIsAPodTrailAtHalfTheWidth()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;
            var trailSystem = SEntMan.System<KsTrailSystem>();

            Assert.That(SProtoMan.Index(MortarShell).TryGetComponent<SupplyPodComponent>(out var shellPod, factory));
            Assert.That(SProtoMan.Index(CargoPod).TryGetComponent<SupplyPodComponent>(out var supplyPod, factory));
            Assert.That(shellPod!.TrailProto, Is.EqualTo(MortarShellTrail), "a mortar shell should leave its own trail");
            Assert.That(supplyPod!.TrailProto, Is.Not.Null.And.Not.EqualTo(MortarShellTrail), "supply pods should keep theirs");

            var podTrailPrototype = SProtoMan.Index(supplyPod.TrailProto!.Value);
            var shellTrailPrototype = SProtoMan.Index(MortarShellTrail);
            Assert.That(shellTrailPrototype.Parents, Does.Contain(podTrailPrototype.ID), "the shell's trail should be the pod's, changed");
            Assert.That(podTrailPrototype.TryGetComponent<KsTrailComponent>(out var podTrail, factory));
            Assert.That(shellTrailPrototype.TryGetComponent<KsTrailComponent>(out var shellTrail, factory));

            // Drawn tile by tile at the sprite's native size, but for its width.
            var nativeSize = Vector2.One;
            var podTile = trailSystem.GetTileSize(podTrail!, nativeSize);
            var shellTile = trailSystem.GetTileSize(shellTrail!, nativeSize);
            Assert.That(shellTile.X / podTile.X, Is.EqualTo(0.5f).Within(1e-4f), "the shell's trail should be half as wide as a pod's");
            Assert.That(shellTile.Y, Is.EqualTo(podTile.Y), "the shell's trail should be as long a tile as a pod's");

            Assert.Multiple(() =>
            {
                Assert.That(shellTrail!.Sprite, Is.EqualTo(podTrail!.Sprite), "otherwise the same: sprite");
                Assert.That(shellTrail.StartSprite, Is.EqualTo(podTrail.StartSprite), "otherwise the same: start sprite");
                Assert.That(shellTrail.Spacing, Is.EqualTo(podTrail.Spacing), "otherwise the same: spacing");
                Assert.That(shellTrail.Color, Is.EqualTo(podTrail.Color), "otherwise the same: colour");
                Assert.That(shellTrail.Blur, Is.EqualTo(podTrail.Blur), "otherwise the same: blur");
                Assert.That(shellTrail.BlurScale, Is.EqualTo(podTrail.BlurScale), "otherwise the same: blur scale");
                Assert.That(shellTrail.TailFadeTiles, Is.EqualTo(podTrail.TailFadeTiles), "otherwise the same: tail fade");
                Assert.That(shellTrail.RevealFadeInFraction, Is.EqualTo(podTrail.RevealFadeInFraction), "otherwise the same: reveal fade");
            });
        });
    }
}
