using Content.Shared.Wires;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.Wires.UI
{
    public sealed partial class WiresBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private WiresMenu? _menu;

        // KS14 start: the tgui layout, picked when the window opens
        private Content.Client._KS14.Tgui.Windows.Wires.KsTguiWiresWindow? _ksTguiMenu;
        // KS14 end

        public WiresBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();
            // KS14 start: tgui theme gets its own window
            if (Content.Client._KS14.Tgui.KsTguiTheme.ReadEnabled())
            {
                _ksTguiMenu = this.CreateWindow<Content.Client._KS14.Tgui.Windows.Wires.KsTguiWiresWindow>();
                _ksTguiMenu.OnAction += PerformAction;
                return;
            }
            // KS14 end
            _menu = this.CreateWindow<WiresMenu>();
            _menu.OnAction += PerformAction;
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);
            _menu?.Populate((WiresBoundUserInterfaceState)state);
            _ksTguiMenu?.Populate((WiresBoundUserInterfaceState)state); // KS14: tgui window
        }

        public void PerformAction(int id, WiresAction action)
        {
            SendMessage(new WiresActionMessage(id, action));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            _menu?.Dispose();
            _ksTguiMenu?.Dispose(); // KS14: tgui window
        }
    }
}
