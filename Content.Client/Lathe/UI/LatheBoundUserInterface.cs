using Content.Client._KS14.Tgui; // KS14
using Content.Client._KS14.Tgui.Windows.Lathe; // KS14
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Content.Shared._KS14.Research.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Lathe.UI
{
    [UsedImplicitly]
    public sealed partial class LatheBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private IKsLatheWindow? _menu; // KS14: LatheMenu -> IKsLatheWindow, the tgui theme has a window of its own
        public LatheBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            // KS14 start: the tgui theme opens its own window
            _menu = KsTguiTheme.ReadEnabled()
                ? this.CreateWindowCenteredRight<KsTguiLatheWindow>()
                : this.CreateWindowCenteredRight<LatheMenu>();
            // KS14 end
            _menu.SetEntity(Owner);

            _menu.OnServerListButtonPressed += _ =>
            {
                SendPredictedMessage(new ConsoleServerSelectionMessage()); // KS14: SendMessage -> SendPredictedMessage, opens the server list at once
            };

            _menu.RecipeQueueAction += (recipe, amount) =>
            {
                SendMessage(new LatheQueueRecipeMessage(recipe, amount));
            };
            _menu.KsRecipeRequestAction += recipe => SendMessage(new Content.Shared._KS14.SPAM.KsLatheRequestMaterialsMessage(recipe)); // KS14
            _menu.QueueDeleteAction += index => SendMessage(new LatheDeleteRequestMessage(index));
            _menu.QueueMoveUpAction += index => SendMessage(new LatheMoveRequestMessage(index, -1));
            _menu.QueueMoveDownAction += index => SendMessage(new LatheMoveRequestMessage(index, 1));
            _menu.DeleteFabricatingAction += () => SendMessage(new LatheAbortFabricationMessage());
            Update(); // KS14
        }

        // KS14 start: a smelter shows the mining points it holds in place of the queue
        public override void Update()
        {
            base.Update();

            if (_menu != null && EntMan.TryGetComponent<Content.Shared._KS14.SPAM.KsSPAMSmelterComponent>(Owner, out var smelterComponent))
                _menu.KsSetSmelterPoints((int)smelterComponent.Unclaimed);
        }
        // KS14 end

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            switch (state)
            {
                case LatheUpdateState msg:
                    if (_menu != null)
                        _menu.SetRecipes(msg.Recipes); // KS14: field -> method, the interface has no fields
                    _menu?.PopulateRecipes();
                    _menu?.UpdateCategories();
                    _menu?.PopulateQueueList(msg.Queue);
                    _menu?.SetQueueInfo(msg.CurrentlyProducing);
                    break;
            }
        }
    }
}
