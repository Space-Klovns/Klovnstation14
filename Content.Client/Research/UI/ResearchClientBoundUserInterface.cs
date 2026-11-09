using Content.Shared.Research.Components;
using Content.Shared.Research.Systems;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.Research.UI
{
    public sealed partial class ResearchClientBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private ResearchClientServerSelectionMenu? _menu;

        public ResearchClientBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
            // KS14: removed the ResearchClientSyncMessage, the server list is networked component state
        }

        protected override void Open()
        {
            base.Open();
            _menu = this.CreateWindow<ResearchClientServerSelectionMenu>();
            _menu.OnServerSelected += SelectServer;
            _menu.OnServerDeselected += DeselectServer;

            Update(); // KS14: there is no state to open with, the list is read from components
        }

        public void SelectServer(int serverId)
        {
            SendMessage(new ResearchClientServerSelectedMessage(serverId));
        }

        public void DeselectServer()
        {
            SendMessage(new ResearchClientServerDeselectedMessage());
        }

        // KS14 start: the list and the selection come from networked components, not a BUI state
        public override void Update()
        {
            base.Update();

            if (_menu == null)
                return;

            var servers = EntMan.System<SharedResearchSystem>().GetServerList(Owner);
            var names = new string[servers.Count];
            var ids = new int[servers.Count];
            for (var i = 0; i < servers.Count; i++)
            {
                names[i] = servers[i].Name;
                ids[i] = servers[i].Id;
            }

            var selectedId = EntMan.TryGetComponent<ResearchClientComponent>(Owner, out var clientComponent)
                ? clientComponent.ConnectedServerId
                : -1;

            _menu.Populate(servers.Count, names, ids, selectedId);
        }
        // KS14 end
    }
}
