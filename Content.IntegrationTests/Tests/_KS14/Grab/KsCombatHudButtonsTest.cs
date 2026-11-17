#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Hud;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ThrowMode;
using NUnit.Framework;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Player;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Grab;

/// <summary>
///     The throw and resist buttons on a real game screen: present, shown for a player who can throw, throw above
///         resist, right after the hotbar's main slots.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCombatHudButtons))]
public sealed class KsCombatHudButtonsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, DummyTicker = false, Dirty = true };

    [Test]
    public async Task ButtonsShowBesideTheHotbar()
    {
        await Server.WaitAssertion(() =>
        {
            var player = Server.ResolveDependency<ISharedPlayerManager>().Sessions.Single().AttachedEntity;
            Assert.That(player, Is.Not.Null);
            Assert.That(SEntMan.HasComponent<KsThrowModeComponent>(player!.Value), "This test needs a player who can throw.");
        });

        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var screen = Client.ResolveDependency<IUserInterfaceManager>().ActiveScreen;
            Assert.That(screen, Is.Not.Null);

            var buttons = Find<KsCombatHudButtons>(screen!);
            Assert.That(buttons, Is.Not.Null, "The game screen must have the throw and resist buttons.");
            Assert.That(buttons!.Visible && buttons.ThrowButton.Visible && buttons.ResistButton.Visible,
                "The buttons must show for a player who can throw.");
            Assert.That(buttons.ThrowButton.GetPositionInParent(), Is.LessThan(buttons.ResistButton.GetPositionInParent()),
                "Throw must sit above resist.");

            // Together, exactly one inventory slot.
            var slot = SlotControl.DefaultButtonSize;
            Assert.That(buttons.ThrowButton.Size, Is.EqualTo(new Vector2(slot, slot / 2f)), "Each button must be half a slot.");
            Assert.That(buttons.ResistButton.Size, Is.EqualTo(new Vector2(slot, slot / 2f)), "Each button must be half a slot.");
            Assert.That(buttons.Size.Y, Is.EqualTo(slot), "The two buttons must stack to one slot's height.");

            var hotbar = Find<HotbarGui>(screen!);
            Assert.That(hotbar, Is.Not.Null);
            var parent = buttons.Parent!;
            var siblings = parent.Children.ToList();
            var index = siblings.IndexOf(buttons);
            Assert.That(index, Is.GreaterThan(0));
            Assert.That(siblings[index - 1].Name, Is.EqualTo("MainHotbar"), "The buttons must come right after the main hotbar slots.");
        });
    }

    /// <summary>
    ///     The option in the Klovnstation 14 tab hides both buttons and brings them back, live.
    /// </summary>
    [Test]
    public async Task OptionHidesTheButtons()
    {
        await Pair.RunTicksSync(10);
        var config = Client.ResolveDependency<IConfigurationManager>();
        KsCombatHudButtons? buttons = null;

        await Client.WaitAssertion(() =>
        {
            buttons = Find<KsCombatHudButtons>(Client.ResolveDependency<IUserInterfaceManager>().ActiveScreen!);
            Assert.That(buttons, Is.Not.Null);
            Assert.That(buttons!.Visible, "The buttons must show by default.");
            config.SetCVar(KsCCVars.CombatHudButtons, false);
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(buttons!.Visible, Is.False, "Turning the option off must hide the buttons.");
            config.SetCVar(KsCCVars.CombatHudButtons, true);
        });
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
            Assert.That(buttons!.Visible && buttons.ResistButton.Visible, "Turning the option back on must show them again."));
    }

    /// <summary>
    ///     Every state the buttons switch between is in the RSI, so neither ever draws as nothing.
    /// </summary>
    [Test]
    public async Task ButtonStatesExist()
    {
        await Client.WaitAssertion(() =>
        {
            var rsi = Client.ResolveDependency<IResourceCache>()
                .GetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / "_KS14/Interface/Hud/combat_buttons.rsi").RSI;
            foreach (var state in new[] { "throw_off", "throw_on", "resist_off", "resist_on" })
                Assert.That(rsi.TryGetState(state, out _), $"combat_buttons.rsi is missing {state}.");
        });
    }

    private static T? Find<T>(Control control) where T : Control
    {
        if (control is T found)
            return found;

        foreach (var child in control.Children)
        {
            if (Find<T>(child) is { } inChild)
                return inChild;
        }

        return null;
    }
}
