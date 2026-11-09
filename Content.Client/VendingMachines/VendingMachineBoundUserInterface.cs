using Content.Client.UserInterface.Controls;
using Content.Client.VendingMachines.UI;
using Content.Shared.VendingMachines;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using System.Linq;

namespace Content.Client.VendingMachines
{
    public sealed partial class VendingMachineBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private VendingMachineMenu? _menu;

        [ViewVariables]
        private Content.Client._KS14.Tgui.Windows.Vending.KsTguiVendingWindow? _tguiMenu; // KS14: the tgui layout, used when the theme is on

        [ViewVariables]
        private List<VendingMachineInventoryEntry> _cachedInventory = new();

        public VendingMachineBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            // KS14 start: tgui layout
            if (Content.Client._KS14.Tgui.KsTguiTheme.ReadEnabled())
            {
                _tguiMenu = this.CreateWindowCenteredLeft<Content.Client._KS14.Tgui.Windows.Vending.KsTguiVendingWindow>();
                _tguiMenu.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
                _tguiMenu.OnVend += (type, id) => SendPredictedMessage(new VendingMachineEjectMessage(type, id));
                Refresh();
                return;
            }
            // KS14 end

            _menu = this.CreateWindowCenteredLeft<VendingMachineMenu>();
            _menu.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
            _menu.OnItemSelected += OnItemSelected;
            Refresh();
        }

        public void Refresh()
        {
            var enabled = EntMan.TryGetComponent(Owner, out VendingMachineComponent? bendy) && !bendy.Ejecting;

            var system = EntMan.System<VendingMachineSystem>();
            _cachedInventory = system.GetAllInventory(Owner);

            _menu?.Populate(_cachedInventory, enabled);
            _tguiMenu?.Populate(_cachedInventory, enabled); // KS14
        }

        public void UpdateAmounts()
        {
            var enabled = EntMan.TryGetComponent(Owner, out VendingMachineComponent? bendy) && !bendy.Ejecting;

            var system = EntMan.System<VendingMachineSystem>();
            _cachedInventory = system.GetAllInventory(Owner);
            _menu?.UpdateAmounts(_cachedInventory, enabled);
            _tguiMenu?.UpdateAmounts(_cachedInventory, enabled); // KS14
        }

        private void OnItemSelected(GUIBoundKeyEventArgs args, ListData data)
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            if (data is not VendorItemsListData { ItemIndex: var itemIndex })
                return;

            if (_cachedInventory.Count == 0)
                return;

            var selectedItem = _cachedInventory.ElementAtOrDefault(itemIndex);

            if (selectedItem == null)
                return;

            SendPredictedMessage(new VendingMachineEjectMessage(selectedItem.Type, selectedItem.ID));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            _tguiMenu?.Dispose(); // KS14

            if (_menu == null)
                return;

            _menu.OnItemSelected -= OnItemSelected;
            _menu.OnClose -= Close;
            _menu.Dispose();
        }
    }
}
