using Content.Client._KS14.Tgui.Windows.Atmos; // KS14
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.IdentityManagement;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.Atmos.UI
{
    /// <summary>
    /// Initializes a <see cref="GasCanisterWindow"/> and updates it when new server messages are received.
    /// </summary>
    [UsedImplicitly]
    public sealed partial class GasCanisterBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private IKsGasCanisterWindow? _window; // KS14: GasCanisterWindow -> the interface, which the tgui window shares

        public GasCanisterBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
            EntMan.EntitySysManager.Resolve(ref _batterySystem); // KS14
        }

        protected override void Open()
        {
            base.Open();

            _window = KsTguiAtmosWindows.Create<GasCanisterWindow, KsTguiGasCanisterWindow, IKsGasCanisterWindow>(this); // KS14

            _window.ReleaseValveCloseButtonPressed += OnReleaseValveClosePressed;
            _window.ReleaseValveOpenButtonPressed += OnReleaseValveOpenPressed;
            _window.ReleasePressureSet += OnReleasePressureSet;
            _window.TankEjectButtonPressed += OnTankEjectPressed;
            _window.TankInsertButtonPressed += OnTankInsertPressed; // KS14
            _window.ShieldToggleButtonPressed += OnToggleShieldingPressed; // KS14
        }

        private void OnTankEjectPressed()
        {
            SendPredictedMessage(new GasCanisterHoldingTankEjectMessage());
        }

        // KS14 start: the tgui window's eject button is an insert button when the tank is out
        private void OnTankInsertPressed()
        {
            SendPredictedMessage(new Content.Shared.Containers.ItemSlots.ItemSlotButtonPressedEvent(
                EntMan.GetComponent<GasCanisterComponent>(Owner).ContainerName, tryEject: false, tryInsert: true));
        }
        // KS14 end

        private void OnReleasePressureSet(float value)
        {
            SendPredictedMessage(new GasCanisterChangeReleasePressureMessage(value));
        }

        private void OnReleaseValveOpenPressed()
        {
            SendPredictedMessage(new GasCanisterChangeReleaseValveMessage(true));
        }

        private void OnReleaseValveClosePressed()
        {
            SendPredictedMessage(new GasCanisterChangeReleaseValveMessage(false));
        }

        /// <summary>
        /// Update the UI state based on server-sent info
        /// </summary>
        /// <param name="state"></param>
        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);
            if (_window == null || state is not GasCanisterBoundUserInterfaceState cast || !EntMan.TryGetComponent(Owner, out GasCanisterComponent? component))
                return;

            var canisterLabel = Identity.Name(Owner, EntMan);
            var tankLabel = component.GasTankSlot.Item != null ? Identity.Name(component.GasTankSlot.Item.Value, EntMan) : null;

            _window.SetCanisterLabel(canisterLabel);
            _window.SetCanisterPressure(cast.CanisterPressure);
            _window.SetPortStatus(cast.PortStatus);

            _window.SetTankLabel(tankLabel);
            _window.SetTankPressure(cast.TankPressure);
            _window.SetReleasePressureRange(component.MinReleasePressure, component.MaxReleasePressure);
            // KS14 start: the limits the tgui window's gauges are drawn from
            _window.SetCanisterLimit(component.Overpressure);
            if (component.GasTankSlot.Item is { } tankUid && EntMan.TryGetComponent(tankUid, out Content.Shared.Atmos.Components.GasTankComponent? tankComponent))
                _window.SetTankLimits(tankComponent.SafetyPressure, tankComponent.Overpressure);
            // KS14 end
            _window.SetReleasePressure(component.ReleasePressure);
            _window.SetReleaseValve(component.ReleaseValveOpen);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            _window?.Dispose();
        }
    }
}
