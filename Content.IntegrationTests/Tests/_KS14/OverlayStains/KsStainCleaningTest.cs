#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.OverlayStains;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.OverlayStains;

/// <summary>
///     Which reagents wash a stain off is up to the reagents, through the <see cref="StainSystem.CleaningGroup"/> reactive
///         group. A stained entity joins it, and leaves it again once clean.
/// </summary>
[TestFixture]
[TestOf(typeof(StainSystem))]
public sealed class KsStainCleaningTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly ProtoId<ReagentPrototype> Water = "Water";
    private static readonly ProtoId<ReagentPrototype> SpaceCleaner = "SpaceCleaner";
    private static readonly ProtoId<ReagentPrototype> Bleach = "Bleach";
    private static readonly ProtoId<ReagentPrototype> Ethanol = "Ethanol";

    private readonly EntProtoId _stainable = "KsTestStainable";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestStainable
  components:
  - type: Stainable
";

    private async Task<EntityUid> SpawnStainedAsync()
    {
        EntityUid uid = default;
        await Server.WaitPost(() =>
        {
            uid = SEntMan.SpawnEntity(_stainable, MapCoordinates.Nullspace);
            SEntMan.System<StainSystem>().ApplyOffsetStain(uid, Vector2.UnitX, Color.Red);
        });

        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<StainedComponent>(uid), Is.True, "precondition: stained"));
        return uid;
    }

    private async Task TouchAsync(EntityUid uid, ProtoId<ReagentPrototype> reagent)
    {
        await Server.WaitPost(() => SEntMan.System<ReactiveSystem>().ReactionEntity(uid, ReactionMethod.Touch, new ReagentQuantity(reagent, 5)));
        await Pair.RunTicksSync(2); // the group is removed deferred
    }

    [Test]
    [TestCase("Water")]
    [TestCase("SpaceCleaner")]
    [TestCase("Bleach")]
    public async Task CleaningReagentsWashStainsOff(string reagent)
    {
        var uid = await SpawnStainedAsync();

        await TouchAsync(uid, new ProtoId<ReagentPrototype>(reagent));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<StainedComponent>(uid), Is.False, $"{reagent} cleans");
            Assert.That(SEntMan.HasComponent<ReactiveComponent>(uid), Is.False, "the reactive component added for the stain goes with it");
        });
    }

    [Test]
    public async Task OtherReagentsLeaveStainsAlone()
    {
        var uid = await SpawnStainedAsync();

        await TouchAsync(uid, Ethanol);

        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<StainedComponent>(uid), Is.True));
    }

    [Test]
    public async Task AnEntityThatWasAlreadyReactiveLeavesTheGroupOnceClean()
    {
        EntityUid uid = default;
        await Server.WaitPost(() =>
        {
            uid = SEntMan.SpawnEntity(_stainable, MapCoordinates.Nullspace);
            SEntMan.EnsureComponent<ReactiveComponent>(uid);
            SEntMan.System<StainSystem>().ApplyOffsetStain(uid, Vector2.UnitX, Color.Red);
        });

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<ReactiveComponent>(uid).ReactiveGroups, Does.ContainKey(StainSystem.CleaningGroup), "precondition: in the group while stained"));

        await TouchAsync(uid, Water);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<StainedComponent>(uid), Is.False);
            Assert.That(SEntMan.TryGetComponent<ReactiveComponent>(uid, out var reactiveComponent), Is.True, "a reactive component it already had stays");
            Assert.That(reactiveComponent!.ReactiveGroups, Does.Not.ContainKey(StainSystem.CleaningGroup), "but not the group");
        });
    }

    /// <summary>
    ///     The cleaning reagents get their reactive effect from partials of the upstream prototypes. A partial that
    ///         stopped applying, or that replaced the prototype instead, would look like nothing changed.
    /// </summary>
    [Test]
    public void PartialsKeepUpstreamReagentData()
    {
        foreach (var reagent in new[] { Water, SpaceCleaner, Bleach })
        {
            var prototype = SProtoMan.Index(reagent);
            Assert.That(prototype.ReactiveEffects, Does.ContainKey(StainSystem.CleaningGroup), $"{reagent} cleans stains");
            Assert.That(prototype.SubstanceColor, Is.Not.EqualTo(default(Color)), $"{reagent} keeps its upstream fields");
        }

        Assert.That(SProtoMan.Index(Water).Metabolisms, Is.Not.Null, "water keeps its upstream metabolisms");
    }
}
