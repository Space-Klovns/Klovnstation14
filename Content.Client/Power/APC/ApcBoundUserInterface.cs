using Content.Client.Power.APC.UI;
using Content.Shared.Access.Systems;
using Content.Shared.APC;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Player;

namespace Content.Client.Power.APC
{
    [UsedImplicitly]
    public sealed partial class ApcBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private Content.Client._KS14.Tgui.Windows.Apc.IKsApcWindow? _menu; /* KS14: ApcMenu -> either window */

        public ApcBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();
            _menu = Content.Client._KS14.Tgui.KsTguiTheme.ReadEnabled() /* KS14: tgui window */
                ? this.CreateWindow<Content.Client._KS14.Tgui.Windows.Apc.KsTguiApcWindow>()
                : this.CreateWindow<ApcMenu>();
            _menu.SetEntity(Owner);
            _menu.OnBreaker += BreakerPressed;

            var hasAccess = false;
            if (PlayerManager.LocalEntity != null)
            {
                var accessReader = EntMan.System<AccessReaderSystem>();
                hasAccess = accessReader.IsAllowed((EntityUid)PlayerManager.LocalEntity, Owner);
            }
            _menu?.SetAccessEnabled(hasAccess);
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            var castState = (ApcBoundInterfaceState)state;
            _menu?.UpdateState(castState);
        }

        public void BreakerPressed()
        {
            SendMessage(new ApcToggleMainBreakerMessage());
        }
    }
}
