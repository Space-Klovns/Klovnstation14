#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.ZLevel;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     The z-level stack itself: ordering, the guards on the one method that mutates it, and the depth walk
///         the renderer and the sprite compensation both read.
/// </summary>
public sealed class KsZLevelStackTest : KsZLevelTestBase
{
    /// <summary>
    ///     The sawmill <see cref="KsZLevelSystem"/> logs its stack rejections to.
    /// </summary>
    private const string ZLevelSawmill = "system.ks_z_level";

    [Test]
    public async Task TestAddedLevelEndsUpUnderTheTarget()
    {
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var upperComponent = entManager.GetComponent<KsZLevelComponent>(stack.UpperMapUid);
            var lowerComponent = entManager.GetComponent<KsZLevelComponent>(stack.LowerMapUid);

            Assert.Multiple(() =>
            {
                // The list is ascending, so First is the bottom-most z-level.
                Assert.That(upperComponent.Node?.Previous?.Value.Owner, Is.EqualTo(stack.LowerMapUid),
                    "AddZLevelDirectlyUnder should put the added z-level below the target, not above it");
                Assert.That(lowerComponent.Node?.Next?.Value.Owner, Is.EqualTo(stack.UpperMapUid),
                    "the link has to be walkable both ways: the upper z-level should be the lower one's Next");
                Assert.That(lowerComponent.Node?.Previous, Is.Null, "nothing should be below the lower z-level");
                Assert.That(upperComponent.Node?.Next, Is.Null, "nothing should be above the upper z-level");
            });
        });
    }

    /// <summary>
    ///     Every z-level in a stack has to point at the same LinkedList object; two that merely look alike are
    ///         not the same stack, and everything that walks the stack quietly breaks if they diverge.
    /// </summary>
    [Test]
    public async Task TestBothLevelsShareOneStackObject()
    {
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var upperComponent = entManager.GetComponent<KsZLevelComponent>(stack.UpperMapUid);
            var lowerComponent = entManager.GetComponent<KsZLevelComponent>(stack.LowerMapUid);

            Assert.That(upperComponent.AssociatedStack, Is.SameAs(lowerComponent.AssociatedStack),
                "the two z-levels are in one stack, so they must share one list instance");
            Assert.That(upperComponent.AssociatedStack, Has.Count.EqualTo(2),
                "the shared stack should hold exactly the two z-levels that were linked, and nothing else");
        });
    }

    [Test]
    public async Task TestCannotAddALevelToAStackItIsAlreadyIn()
    {
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();

        // Each rejection below logs an error on purpose. Only that exact error is tolerated; anything else
        //      the server logs still fails the test.
        using var expectedErrors = ExpectServerError(ZLevelSawmill, "to a stack it is already part of");

        await server.WaitAssertion(() =>
        {
            var associatedStack = entManager.GetComponent<KsZLevelComponent>(stack.UpperMapUid).AssociatedStack;

            Assert.Multiple(() =>
            {
                Assert.That(zLevelSystem.AddZLevelDirectlyUnder(stack.UpperMapUid, stack.LowerMapUid), Is.False,
                    "re-adding a z-level to the stack it is already in would migrate it out of and back into the same list");
                Assert.That(zLevelSystem.AddZLevelDirectlyAbove(stack.LowerMapUid, stack.UpperMapUid), Is.False,
                    "stating the existing relationship the other way round is still a re-add, and still wrong");
                Assert.That(zLevelSystem.AddZLevelAboveStack(stack.UpperMapUid, stack.LowerMapUid), Is.False,
                    "the whole-stack variants have to reject a member of that stack too, not just the direct ones");
                Assert.That(zLevelSystem.AddZLevelUnderStack(stack.UpperMapUid, stack.LowerMapUid), Is.False,
                    "moving a z-level to the bottom of the stack it is already in is the same re-add in disguise");
            });

            Assert.That(associatedStack, Has.Count.EqualTo(2), "a rejected add should leave the stack untouched");
        });

        Assert.That(expectedErrors.Matches, Is.EqualTo(4),
            "every rejected add should have complained loudly about it");
    }

    [Test]
    public async Task TestCannotAddALevelRelativeToItself()
    {
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();

        using var expectedErrors = ExpectServerError(ZLevelSawmill, "relative to itself");

        await server.WaitAssertion(() =>
        {
            Assert.That(zLevelSystem.AddZLevelDirectlyUnder(stack.UpperMapUid, stack.UpperMapUid), Is.False,
                "a z-level cannot be stacked relative to itself");
            Assert.That(entManager.GetComponent<KsZLevelComponent>(stack.UpperMapUid).AssociatedStack,
                Has.Count.EqualTo(2), "a rejected add should leave the stack untouched");
        });

        Assert.That(expectedErrors.Matches, Is.EqualTo(1),
            "the rejected add should have complained loudly about it");
    }

    [Test]
    public async Task TestZLevelsBelowIsAscendingAndExcludesSelf()
    {
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();

        await server.WaitAssertion(() =>
        {
            var below = new List<Entity<KsZLevelComponent>>();
            zLevelSystem.TryGetZLevelsBelow(stack.UpperMapUid, below);

            Assert.That(below.Select(x => x.Owner), Is.EquivalentTo(new[] { stack.LowerMapUid }),
                "only the lower z-level is below the upper one, and the upper one is not below itself");

            var noneBelow = new List<Entity<KsZLevelComponent>>();
            zLevelSystem.TryGetZLevelsBelow(stack.LowerMapUid, noneBelow);

            Assert.That(noneBelow, Is.Empty,
                "the lower z-level is the bottom of the stack, so nothing should be reported below it");
        });
    }

    /// <summary>
    ///     The depth walk the renderer scales passes by and the sprite compensation reads. Stepping down one
    ///         z-level crosses the lower one's own Depth, which defaults to 1.
    /// </summary>
    [Test]
    public async Task TestDepthBelowWalksTheStack()
    {
        var stack = await CreateStack();

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(zLevelSystem.TryGetDepthBelow(stack.UpperMapUid, stack.UpperMapUid, out var ownDepth), Is.True,
                    "a z-level should resolve against itself rather than being reported as unreachable");
                Assert.That(ownDepth, Is.EqualTo(0f),
                    "a z-level is not below itself, so it sits at zero depth from its own viewpoint");

                Assert.That(zLevelSystem.TryGetDepthBelow(stack.UpperMapUid, stack.LowerMapUid, out var belowDepth), Is.True,
                    "the lower z-level is one step down the same stack, so the walk should reach it");
                Assert.That(belowDepth, Is.EqualTo(1f).Within(0.0001f),
                    "one z-level down at the default Depth of 1");

                Assert.That(zLevelSystem.TryGetDepthBelow(stack.LowerMapUid, stack.UpperMapUid, out _), Is.False,
                    "the upper z-level is above the lower one, not below it");
            });
        });
    }

    /// <summary>
    ///     Render depth has to fall as a z-level gets closer, and reach the eye's own scale at depth zero.
    /// </summary>
    [Test]
    public async Task TestDepthScaleShrinksWithDistance()
    {
        var zLevelSystem = Pair.Server.ResolveDependency<IEntityManager>().System<KsZLevelSystem>();
        var eyeScale = System.Numerics.Vector2.One;

        await Pair.Server.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(zLevelSystem.GetDepthScale(eyeScale, 0f), Is.EqualTo(eyeScale),
                "a z-level at the viewer's own depth renders at the eye's own scale");
            Assert.That(zLevelSystem.GetDepthScale(eyeScale, 1f).X, Is.LessThan(eyeScale.X),
                "one z-level down has to render smaller than the viewer's own, or there is no sense of depth");
            Assert.That(zLevelSystem.GetDepthScale(eyeScale, 2f).X,
                Is.LessThan(zLevelSystem.GetDepthScale(eyeScale, 1f).X),
                "the further down a z-level is, the smaller it should render");

            // A gap crossing overhead passes a negative depth, and is drawn larger for it.
            Assert.That(zLevelSystem.GetDepthScale(eyeScale, -1f).X, Is.GreaterThan(eyeScale.X),
                "something above the viewer is nearer the camera, so it has to render larger");
        }));
    }
}
