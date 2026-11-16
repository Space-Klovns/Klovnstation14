using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Windows.LateJoin;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Roles;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The tgui late join window: the lobby gets it under both themes, and every job row and the random button reach
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
                // Both themes get the ported window.
                Assert.That(window, Is.TypeOf<KsTguiLateJoinWindow>());
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

    /// <summary>
    ///     tgui fills the cards and rows with the department's colour; Baron keeps its dark panels and uses the colour only
    ///         as an accent.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TheCardsFollowTheTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        var station = new NetEntity(901);
        await Client.WaitPost(() =>
        {
            var window = new KsTguiLateJoinWindow((_, _) => { });
            try
            {
                window.Populate(
                    new Dictionary<NetEntity, string> { [station] = "First" },
                    new Dictionary<NetEntity, Dictionary<ProtoId<JobPrototype>, int?>>
                    {
                        [station] = new() { [JanitorJob] = 2 },
                    });

                var entry = window.JobEntries.Single();
                var department = Client.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<DepartmentPrototype>()
                    .First(department => department.Roles.Contains(JanitorJob));
                var (departmentColor, _) = KsTguiLateJoinWindow.DepartmentLook(department);

                var rowBox = (StyleBoxFlat) entry.StyleBoxOverride!;
                Control? parent = entry;
                PanelContainer? card = null;
                while (parent != null && card == null)
                {
                    parent = parent.Parent;
                    if (parent is PanelContainer { ToolTip: not null } panel)
                        card = panel;
                }

                Assert.That(card, Is.Not.Null, "the job row sits in a department card");
                var cardBox = (StyleBoxFlat) card!.PanelOverride!;

                if (tgui)
                {
                    Assert.That(cardBox.BackgroundColor, Is.EqualTo(departmentColor), "tgui's card is the department's colour");
                    Assert.That(rowBox.BorderThickness.Left, Is.Zero, "tgui's row has no bar");
                }
                else
                {
                    Assert.That(cardBox.BackgroundColor, Is.EqualTo(KsTguiLateJoinWindow.BaronCardBackground),
                        "Baron's card is its dark panel");
                    Assert.That(rowBox.BackgroundColor, Is.Not.EqualTo(KsTguiLateJoinWindow.Darken(departmentColor, 10f)),
                        "Baron's row is not filled with the department's colour");
                    Assert.That(rowBox.BorderColor, Is.EqualTo(departmentColor), "Baron's row has a bar in the department's colour");
                    Assert.That(rowBox.BorderThickness.Left, Is.GreaterThan(0f));
                }
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
        var handler = (Action<BaseButton.ButtonEventArgs>)field.GetValue(button);
        handler?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
    }
}
