using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client.Atmos.UI;
using Content.Client.BarSign.Ui;
using Content.Client.Medical.CrewMonitoring;
using Content.Client.PDA.Ringer;
using Content.Client.Pinpointer.UI;
using Content.Client.Power;
using Content.Client.Power.Generator;
using Content.Client.Radio.Ui;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Reflection;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     A window that states its own height can be too short for what's in it, and the frame clips what doesn't fit.
/// </summary>
[TestOf(typeof(FancyWindow))]
public sealed class KsWindowContentsFitTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     Windows already too short. Only ever take from this: a new window that's cut off gets fixed, not listed.
    /// </summary>
    private static readonly HashSet<Type> KnownCutOffBaron =
    [
        typeof(BarSignMenu),
        typeof(CrewMonitoringWindow),
        typeof(GasPressureRegulatorWindow),
        typeof(GeneratorWindow),
        typeof(NavMapBeaconWindow),
        typeof(PowerMonitoringWindow),
        typeof(RadarConsoleWindow),
        typeof(RingtoneMenu),
        typeof(StationMapWindow),
    ];

    /// <summary>
    ///     As above, for the tgui style, whose header is a lot shorter than Baron's.
    /// </summary>
    private static readonly HashSet<Type> KnownCutOffTgui =
    [
        typeof(CrewMonitoringWindow),
        typeof(RadarConsoleWindow),
    ];

    /// <summary>
    ///     Windows are measured as built, before any state fills them, so this only sees what they always show.
    /// </summary>
    /// <remarks>
    ///     For each theme, because the header's height is the theme's, and a window that fits under one can clip under the
    ///         other. Fonts differ too: this measures DejaVu Sans where a player with Verdana gets Verdana, so passing doesn't
    ///         prove a tight fit for them.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ContentsFitInTheOpeningSize(bool tgui)
    {
        await OverrideCVar(Side.Client, Content.Shared._KS14.CCVar.KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2); // The theme restyles on the frame after the cvar changes.

        var knownCutOff = tgui ? KnownCutOffTgui : KnownCutOffBaron;

        var reflectionManager = Client.ResolveDependency<IReflectionManager>();
        var modLoader = Client.ResolveDependency<IModLoader>();
        var typeFactory = Client.ResolveDependency<IDynamicTypeFactory>();

        await Client.WaitAssertion(() =>
        {
            var cutOff = new HashSet<Type>();
            var messages = new List<string>();

            foreach (var type in reflectionManager.GetAllChildren(typeof(FancyWindow)))
            {
                if (type.IsAbstract || !modLoader.IsContentType(type) || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                // tgui's own windows never open under Baron, whose header is taller.
                if (!tgui && type.Namespace?.StartsWith("Content.Client._KS14.Tgui") == true)
                    continue;

                var window = (FancyWindow)typeFactory.CreateInstance(type, oneOff: true, inject: false);

                // Closed again before layout runs: arranging an empty window isn't what's being tested, and some
                //      of those it squeezes hit debug asserts of their own.
                window.OpenCentered();
                var missingHeight = MissingHeight(window);
                window.Close();

                if (missingHeight <= 0.5f)
                    continue;

                cutOff.Add(type);
                messages.Add($"{type.Name} is {missingHeight} too short");
            }

            Assert.Multiple(() =>
            {
                Assert.That(cutOff.Except(knownCutOff).ToList(), Is.Empty,
                    $"These windows clip their contents, so leave their height to the contents: {string.Join(", ", messages)}");
                Assert.That(knownCutOff.Except(cutOff).ToList(), Is.Empty,
                    "These windows fit now, so take them off the list");
            });
        });
    }

    /// <summary>
    ///     A clipped control is measured at the clipped size, so ask what the contents need with room to spare.
    /// </summary>
    private static float MissingHeight(FancyWindow window)
    {
        window.ForceRunStyleUpdate();
        window.InvalidateMeasure();
        window.Measure(Vector2Helpers.Infinity);

        var header = window.ContentsContainer.Parent!.GetChild(0);
        var availableHeight = window.DesiredSize.Y - header.DesiredSize.Y;

        var neededHeight = 0f;
        foreach (var child in window.ContentsContainer.Children)
        {
            // Whatever doesn't fit in this scrolls rather than clips.
            if (child is Robust.Client.UserInterface.Controls.ScrollContainer)
                continue;

            child.Measure(new Vector2(window.DesiredSize.X, float.PositiveInfinity));
            neededHeight = MathF.Max(neededHeight, child.DesiredSize.Y);
        }

        return neededHeight - availableHeight;
    }
}
