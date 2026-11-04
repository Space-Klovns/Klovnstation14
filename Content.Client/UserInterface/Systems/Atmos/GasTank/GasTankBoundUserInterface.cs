using Content.Client._KS14.Atmos.UI; // KS14
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.UserInterface.Systems.Atmos.GasTank
{
    [UsedImplicitly]
    public sealed partial class GasTankBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private KsTguiGasTankWindow /* KS14: GasTankWindow -> KsTguiGasTankWindow, tgui port */? _window;

        public GasTankBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        public void SetOutputPressure(float value)
        {
            SendPredictedMessage(new GasTankSetPressureMessage
            {
                Pressure = value
            });
        }

        public void ToggleInternals()
        {
            SendPredictedMessage(new GasTankToggleInternalsMessage());
        }

        protected override void Open()
        {
            base.Open();
            _window = this.CreateWindow<KsTguiGasTankWindow /* KS14: GasTankWindow -> KsTguiGasTankWindow */>();
            _window.Entity = Owner;
            _window.SetTitle(EntMan.GetComponent<MetaDataComponent>(Owner).EntityName);
            _window.OnOutputPressure += SetOutputPressure;
            _window.OnToggleInternals += ToggleInternals;
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (EntMan.TryGetComponent(Owner, out GasTankComponent? component))
            {
                var canConnect = EntMan.System<SharedGasTankSystem>().CanConnectToInternals((Owner, component));
                _window?.Update(canConnect, component.IsConnected, component.ReleasePressure);
            }

            if (state is GasTankBoundUserInterfaceState cast)
                _window?.UpdateState(cast);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _window?.Close();
        }
    }
}
