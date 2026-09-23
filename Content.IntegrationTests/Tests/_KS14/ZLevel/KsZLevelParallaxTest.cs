#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel;
using Robust.Shared.GameObjects;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     How hard the stack falls away below the viewer, and that the knob for it cannot break the stack.
/// </summary>
/// <remarks>
///     A connected pair, unlike the rest of the z-level tests, for two reasons that both come from this
///         being a client-side rendering knob. The cvar is <c>CVar.CLIENT</c>, so only the client may change
///         it and a server-side override would be quietly ignored; and a disconnected client has never
///         started its entity systems, so there is no <see cref="KsZLevelSystem"/> on it to ask.
/// </remarks>
public sealed class KsZLevelParallaxTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    /// <summary>
    ///     That the parallax strength cvar reaches the scale, and that neither end of it can invert the
    ///         stack.
    /// </summary>
    /// <remarks>
    ///     The shrink is linear and unbounded in both depth and strength, so a tall stack or a raised
    ///         strength walks the scale down through zero - past which a pass draws mirrored and grows
    ///         again, which reads as the floors below turning inside out rather than as a stronger effect.
    ///     Zero is the other end and has to stay meaningful: it is how a player turns the effect off, and
    ///         flat is only legible if every level lands on exactly the eye's own scale.
    /// </remarks>
    [Test]
    public async Task TestParallaxStrengthScalesTheDepthEffect()
    {
        var client = Pair.Client;
        var eyeScale = System.Numerics.Vector2.One;

        // Resolved inside the client's own context: an entity system lives on that instance's thread, and
        //      reaching for one from the test thread throws UnregisteredTypeException.
        KsZLevelSystem zLevelSystem = default!;
        var defaultScale = 0f;

        await client.WaitPost(() =>
        {
            zLevelSystem = client.ResolveDependency<IEntityManager>().System<KsZLevelSystem>();
            defaultScale = zLevelSystem.GetDepthScale(eyeScale, 1f).X;
        });

        Assert.That(defaultScale, Is.LessThan(eyeScale.X),
            "the default has to actually shrink, or every comparison below is against nothing");

        await OverrideCVar(Side.Client, KsCCVars.ZLevelParallaxStrength, 0f);
        await client.WaitAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(zLevelSystem.GetDepthScale(eyeScale, 1f), Is.EqualTo(eyeScale),
                "at zero strength every z-level draws at the eye's own scale - flat, but not broken");
            Assert.That(zLevelSystem.GetDepthScale(eyeScale, 8f), Is.EqualTo(eyeScale),
                "and that has to hold however deep the stack is");
        }));

        await OverrideCVar(Side.Client, KsCCVars.ZLevelParallaxStrength, 2f);
        await client.WaitAssertion(() =>
            Assert.That(zLevelSystem.GetDepthScale(eyeScale, 1f).X, Is.LessThan(defaultScale),
                "a higher strength has to shrink a given depth further, or the knob does nothing"));

        // Far past anything sane, which is the point: nothing stops a player typing it.
        await OverrideCVar(Side.Client, KsCCVars.ZLevelParallaxStrength, 50f);
        await client.WaitAssertion(() =>
            Assert.That(zLevelSystem.GetDepthScale(eyeScale, 4f).X, Is.GreaterThan(0f),
                "the scale may never reach zero or go negative - past that a pass draws mirrored and grows again"));

        await OverrideCVar(Side.Client, KsCCVars.ZLevelParallaxStrength, -3f);
        await client.WaitAssertion(() =>
            Assert.That(zLevelSystem.GetDepthScale(eyeScale, 1f).X, Is.EqualTo(eyeScale.X),
                "a negative strength is not a stronger effect but an inverted one, so it clamps to off"));

        await OverrideCVar(Side.Client, KsCCVars.ZLevelParallaxStrength, 1f);
    }
}
