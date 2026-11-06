using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Windows.LateJoin;
using Content.Client.LateJoin;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The tgui late join window: the lobby gets it only while tgui is on, and every job row and the random button reach
///         the same selection the stock window makes.
/// </summary>
[TestOf(typeof(KsTguiLateJoinWindow))]
public sealed class KsTguiLateJoinTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly ProtoId<JobPrototype> CaptainJob = "Captain";
    private static readonly ProtoId<JobPrototype> JanitorJob = "Janitor";
    private static readonly ProtoId<JobPrototype> ClownJob = "Clown";

    [TestCase(true)]
    [TestCase(false)]
    public async Task FactoryFollowsTheTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        await Client.WaitPost(() =>
        {
            // The stock window warns that no stations exist, and a warning fails a test.
            Client.ResolveDependency<ILogManager>().GetSawmill("latejoin.panel").Level = LogLevel.Error;

            var window = KsLateJoinWindowFactory.Create();
            try
            {
                if (tgui)
                    Assert.That(window, Is.TypeOf<KsTguiLateJoinWindow>());
                else
                    Assert.That(window, Is.TypeOf<LateJoinGui>());
            }
            finally
            {
                window.Dispose();
            }
        });
    }

    [Test]
    public async Task EveryJobRowAndRandomSelect()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var firstStation = new NetEntity(901);
        var secondStation = new NetEntity(902);
        var selected = new List<(NetEntity Station, string Job)>();

        await Client.WaitPost(() =>
        {
            var window = new KsTguiLateJoinWindow((station, job) => selected.Add((station, job)));
            try
            {
                window.Populate(
                    new Dictionary<NetEntity, string> { [firstStation] = "First", [secondStation] = "Second" },
                    new Dictionary<NetEntity, Dictionary<ProtoId<JobPrototype>, int?>>
                    {
                        [firstStation] = new()
                        {
                            [CaptainJob] = 1,
                            [JanitorJob] = null,
                            [ClownJob] = 0,
                        },
                        [secondStation] = new() { [JanitorJob] = 2 },
                    });

                var entries = window.JobEntries.ToList();
                Assert.That(entries, Has.Count.GreaterThanOrEqualTo(4));

                foreach (var entry in entries)
                {
                    var expectedDisabled = entry.Amount == 0;
                    Assert.That(entry.Disabled, Is.EqualTo(expectedDisabled), entry.JobId);
                }

                // The open rows each select their own station and job; a full one is not pressable.
                foreach (var entry in entries.Where(entry => !entry.Disabled))
                    Press(entry);

                Assert.That(selected, Has.Member((firstStation, (string)CaptainJob)));
                Assert.That(selected, Has.Member((firstStation, (string)JanitorJob)));
                Assert.That(selected, Has.Member((secondStation, (string)JanitorJob)));
                Assert.That(selected.Any(pick => pick.Job == ClownJob), Is.False);

                // Random picks an open job on the station that is shown (the first).
                selected.Clear();
                Press(window.RandomButton);
                Assert.That(selected, Has.Count.EqualTo(1));
                Assert.That(selected[0].Station, Is.EqualTo(firstStation));
                Assert.That(selected[0].Job, Is.Not.EqualTo((string)ClownJob));
            }
            finally
            {
                window.Dispose();
            }
        });
    }

    private static void Press(BaseButton button)
    {
        // OnPressed is a field-like event; invoking its delegate is what a click does once it passes the button's own checks.
        var field = typeof(BaseButton).GetField(nameof(BaseButton.OnPressed), BindingFlags.Instance | BindingFlags.NonPublic);
        var handler = (Action<BaseButton.ButtonEventArgs>?)field!.GetValue(button);
        handler?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
    }
}
