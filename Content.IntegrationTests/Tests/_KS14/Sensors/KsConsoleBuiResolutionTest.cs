#nullable enable
using Content.Client.Shuttles.BUI;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.Reflection;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

/// <summary>
///     Regression: a fork BoundUserInterface must not be named so that its class
///         name ends with a vanilla BUI's class name. The client resolves a
///         console's BUI from the <c>ClientType</c> string via
///         <see cref="IReflectionManager.LooseGetType"/>, which matches on
///         <c>FullName.EndsWith(name)</c>. A type called
///         <c>KsShuttleConsoleBoundUserInterface</c> therefore also matches the
///         vanilla lookup for <c>"ShuttleConsoleBoundUserInterface"</c>, hijacking
///         the vanilla ComputerShuttle so it opens the KS window (cone toggles, fog).
/// </summary>
public sealed class KsConsoleBuiResolutionTest : GameTest
{
    [Test]
    public async Task VanillaConsoleBuiIsNotHijacked()
    {
        var client = Pair.Client;
        var reflection = client.ResolveDependency<IReflectionManager>();

        await client.WaitAssertion(() =>
        {
            var resolved = reflection.LooseGetType("ShuttleConsoleBoundUserInterface");
            Assert.That(resolved, Is.EqualTo(typeof(ShuttleConsoleBoundUserInterface)),
                $"Vanilla 'ShuttleConsoleBoundUserInterface' resolved to '{resolved.FullName}'. A fork BUI whose "
                + "class name ends with the vanilla name is hijacking it via LooseGetType EndsWith matching.");
        });
    }
}
