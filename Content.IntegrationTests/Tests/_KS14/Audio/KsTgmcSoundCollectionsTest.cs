using System.Linq;
using Content.IntegrationTests.Fixtures;
using NUnit.Framework;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Audio;

/// <summary>
///     TGMC's bullet and gore sounds are added to upstream's collections by partials, which keep upstream's own. A
///         partial that stopped applying (upstream renamed the collection) would look just like the unmodified one.
/// </summary>
[TestFixture]
public sealed class KsTgmcSoundCollectionsTest : GameTest
{
    private static readonly ProtoId<SoundCollectionPrototype> MeatBulletImpact = "MeatBulletImpact";
    private static readonly ProtoId<SoundCollectionPrototype> MetalBulletImpact = "MetalBulletImpact";
    private static readonly ProtoId<SoundCollectionPrototype> BulletMiss = "BulletMiss";
    private static readonly ProtoId<SoundCollectionPrototype> Gib = "gib";

    [Test]
    [TestCase("MeatBulletImpact", "/Audio/Weapons/Guns/Hits/bullet_meat1.ogg", "/Audio/_KS14/Weapons/Guns/Hits/tgmc_bullet_flesh1.ogg")]
    [TestCase("MetalBulletImpact", "/Audio/Weapons/Guns/Hits/ric1.ogg", "/Audio/_KS14/Weapons/Guns/Hits/tgmc_ricochet1.ogg")]
    [TestCase("BulletMiss", "/Audio/Weapons/Guns/Miss/bullet_miss1.ogg", "/Audio/_KS14/Weapons/Guns/Miss/tgmc_bullet_miss1.ogg")]
    [TestCase("gib", "/Audio/_KS14/Effects/Flesh/flesh_break1.ogg", "/Audio/_KS14/Effects/Gore/tgmc_gib.ogg")]
    public async Task CollectionKeepsUpstreamsAndGainsTgmcs(string collection, string upstreamFile, string tgmcFile)
    {
        ProtoId<SoundCollectionPrototype> collectionId = new[] { MeatBulletImpact, MetalBulletImpact, BulletMiss, Gib }.Single(id => id.Id == collection);
        await Server.WaitAssertion(() =>
        {
            var files = Server.ProtoMan.Index(collectionId).PickFiles.Select(path => path.ToString()).ToList();
            Assert.That(files, Does.Contain(upstreamFile), $"{collection} lost upstream's sounds");
            Assert.That(files, Does.Contain(tgmcFile), $"{collection} did not gain TGMC's sounds");
        });
    }
}
