using Content.Client.Chemistry.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Chemistry;
using Content.Shared.Containers.ItemSlots;

namespace Content.IntegrationTests.Tests.Chemistry;

public sealed class DispenserTest : InteractionTest
{
    /// <summary>
    ///     Basic test that checks that a beaker can be inserted and ejected from a dispenser.
    /// </summary>
    [Test]
    public async Task InsertEjectBuiTest()
    {
        // KS14 start: this clicks the stock window, which only the Baron theme opens. The pair reverts the cvar afterwards.
        await Client.WaitPost(() => Client.CfgMan.SetCVar(Content.Shared._KS14.CCVar.KsCCVars.TguiStyle, false));
        await RunTicks(1);
        // KS14 end

        await SpawnTarget("ChemDispenser");
        ToggleNeedPower();

        // Insert beaker
        await InteractUsing("Beaker");
        Assert.That(HandSys.GetActiveItem((SEntMan.GetEntity(Player), Hands)), Is.Null);

        // Open BUI
        await Interact();

        // Eject beaker via BUI.
        var ev = new ItemSlotButtonPressedEvent(SharedReagentDispenser.OutputSlotName);
        await SendBui(ReagentDispenserUiKey.Key, ev);

        // Beaker is back in the player's hands
        Assert.That(HandSys.GetActiveItem((SEntMan.GetEntity(Player), Hands)), Is.Not.Null);
        AssertPrototype("Beaker", SEntMan.GetNetEntity(HandSys.GetActiveItem((SEntMan.GetEntity(Player), Hands))));

        // Re-insert the beaker
        await Interact();
        Assert.That(HandSys.GetActiveItem((SEntMan.GetEntity(Player), Hands)), Is.Null);

        // Re-eject using the button directly instead of sending a BUI event. This test is really just a test of the
        // bui/window helper methods.
        await ClickControl<ReagentDispenserWindow>(nameof(ReagentDispenserWindow.EjectButton));
        await RunTicks(5);
        Assert.That(HandSys.GetActiveItem((SEntMan.GetEntity(Player), Hands)), Is.Not.Null);
        AssertPrototype("Beaker", SEntMan.GetNetEntity(HandSys.GetActiveItem((SEntMan.GetEntity(Player), Hands))));
    }
}
