using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Chem;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.CCVar;
using Content.Shared.Chemistry;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     A slot's eject button is an insert button while the slot is empty, and pressing it puts the held item in.
/// </summary>
[TestOf(typeof(KsTguiItemSlotButton))]
public sealed class KsTguiItemSlotButtonTest : InteractionTest
{
    [Test]
    public async Task InsertButtonPutsTheHeldItemInTheSlot()
    {
        await SpawnTarget("ChemDispenser");
        ToggleNeedPower();
        await Client.WaitPost(() => Client.CfgMan.SetCVar(KsCCVars.TguiStyle, true));

        await Server.WaitPost(() => SEntMan.System<SharedUserInterfaceSystem>().OpenUi(STarget!.Value, ReagentDispenserUiKey.Key, SPlayer));
        await RunTicks(5);

        var window = GetWindow<KsTguiReagentDispenserWindow>();
        var button = window.EjectButton;
        Assert.That(button.Filled, Is.False, "An empty slot offers Insert.");
        Assert.That(button.Disabled, Is.False);

        await PlaceInHands("Beaker");
        await ClickControl(button);
        await RunTicks(5);

        var slots = SEntMan.GetComponent<ItemSlotsComponent>(STarget!.Value);
        Assert.That(slots.Slots[SharedReagentDispenser.OutputSlotName].HasItem, Is.True, "The beaker went into the slot.");
        Assert.That(HandSys.GetActiveItem((SEntMan.GetEntity(Player), Hands)), Is.Null);
        Assert.That(button.Filled, Is.True, "The button became Eject.");

        await ClickControl(button);
        await RunTicks(5);

        Assert.That(slots.Slots[SharedReagentDispenser.OutputSlotName].HasItem, Is.False, "Eject still takes it out.");
        Assert.That(HandSys.GetActiveItem((SEntMan.GetEntity(Player), Hands)), Is.Not.Null);
        Assert.That(button.Filled, Is.False);
    }
}
