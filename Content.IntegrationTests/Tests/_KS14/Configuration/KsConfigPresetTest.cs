using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Content.IntegrationTests.Fixtures;
using Content.Server.Entry;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Configuration;
using Content.Shared.CCVar;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Log;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Configuration;

/// <summary>
///     Client and server load the same config presets, and a preset may set cvars only one of them registers. Each
///         side sets the ones it has and skips the rest without an error, a cvar neither side has is a typo, and the
///         client picks up the server's presets when it connects.
/// </summary>
[TestFixture]
[TestOf(typeof(KsConfigPresets))]
public sealed class KsConfigPresetTest : GameTest
{
    // Fresh and destroyed after: these change cvar defaults, and a recycled pair puts back each changed cvar with
    //     SetCVar, an explicit value that would then outrank any default a later test (or preset) sets.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true, Fresh = true, Destructive = true };

    private static MemoryStream PresetStream(string toml)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(toml));
    }

    /// <summary>
    ///     A TOML table holding one cvar: <c>[klovn.atmos]</c> then <c>puddle_vacuum_pressure = 12.5</c>.
    /// </summary>
    private static string TomlLine(string cvarName, string value)
    {
        var lastDot = cvarName.LastIndexOf('.');
        return $"[{cvarName[..lastDot]}]\n{cvarName[(lastDot + 1)..]} = {value}\n";
    }

    [Test]
    public async Task EveryPresetCVarIsRegisteredOnOneSide()
    {
        var resources = Server.ResolveDependency<IResourceManager>();
        var serverConfig = Server.ResolveDependency<IConfigurationManager>();
        var clientConfig = Client.ResolveDependency<IConfigurationManager>();

        await Server.WaitAssertion(() =>
        {
            var presets = resources.ContentFindFiles(EntryPoint.ConfigPresetsDir).ToList();
            Assert.That(presets, Is.Not.Empty, "precondition: there are presets to check");

            using (Assert.EnterMultipleScope())
            {
                foreach (var preset in presets)
                {
                    foreach (var (name, _) in KsConfigPresets.ReadCVars(resources.ContentFileRead(preset)))
                    {
                        Assert.That(serverConfig.IsCVarRegistered(name) || clientConfig.IsCVarRegistered(name), Is.True,
                            $"{preset} sets '{name}', which neither the server nor the client registers.");
                    }
                }
            }
        });
    }

    [Test]
    public async Task EachSideSetsItsOwnCVarsAndSkipsTheOthers()
    {
        var serverConfig = Server.ResolveDependency<IConfigurationManager>();
        var clientConfig = Client.ResolveDependency<IConfigurationManager>();
        var serverOnly = KsCCVars.AtmosPuddleVacuumPressure;
        var clientOnly = CVars.AudioDefaultConcurrent;
        const float serverOnlyValue = 12.5f; // as written in the TOML below
        const int clientOnlyValue = 37;

        var toml = TomlLine(serverOnly.Name, "12.5") + TomlLine(clientOnly.Name, clientOnlyValue.ToString(System.Globalization.CultureInfo.InvariantCulture));

        await Server.WaitAssertion(() =>
        {
            Assert.That(clientConfig.IsCVarRegistered(serverOnly.Name), Is.False, "precondition: the server cvar is server-only");
            var sawmill = Server.ResolveDependency<ILogManager>().GetSawmill("configpreset");
            Assert.That(KsConfigPresets.LoadDefaults(serverConfig, PresetStream(toml), sawmill), Is.EqualTo(1), "The server didn't set exactly its own cvar.");
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(serverConfig.IsCVarRegistered(clientOnly.Name), Is.False, "precondition: the client cvar is client-only");
            var sawmill = Client.ResolveDependency<ILogManager>().GetSawmill("configpreset");
            Assert.That(KsConfigPresets.LoadDefaults(clientConfig, PresetStream(toml), sawmill), Is.EqualTo(1), "The client didn't set exactly its own cvar.");
            Assert.That(clientConfig.GetCVar(clientOnly), Is.EqualTo(clientOnlyValue), "The client's cvar didn't take the preset's value.");
        });

        await Server.WaitAssertion(() =>
            Assert.That(serverConfig.GetCVar(serverOnly), Is.EqualTo(serverOnlyValue), "The server's cvar didn't take the preset's value."));
    }

    /// <summary>
    ///     The client loads its presets before it connects, so a client cvar in one only the server lists would never
    ///         be set without the server's list, which the client gets once connected.
    /// </summary>
    [Test]
    public async Task TheClientLoadsTheServersPresets()
    {
        const string preset = "KS14/ks14";
        var serverConfig = Server.ResolveDependency<IConfigurationManager>();
        var clientConfig = Client.ResolveDependency<IConfigurationManager>();
        var clientResources = Client.ResolveDependency<IResourceManager>();

        var clientCVars = new List<(string Name, object Value)>();
        await Client.WaitAssertion(() =>
        {
            clientCVars = KsConfigPresets.ReadCVars(clientResources.ContentFileRead($"{EntryPoint.ConfigPresetsDir}{preset}.toml"))
                .Where(cvar => clientConfig.IsCVarRegistered(cvar.Name)
                               && (clientConfig.GetCVarFlags(cvar.Name) & (CVar.REPLICATED | CVar.SERVER)) == 0)
                .ToList();
        });

        if (clientCVars.Count == 0)
            Assert.Ignore($"{preset} sets no client-side cvar to check.");

        await Server.WaitPost(() => serverConfig.SetCVar(CCVars.ConfigPresets, preset));
        await RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var (name, value) in clientCVars)
                {
                    Assert.That(Convert.ToString(clientConfig.GetCVar<object>(name), System.Globalization.CultureInfo.InvariantCulture),
                        Is.EqualTo(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)),
                        $"The client didn't take '{name}' from the server's preset {preset}.");
                }
            }
        });
    }
}
