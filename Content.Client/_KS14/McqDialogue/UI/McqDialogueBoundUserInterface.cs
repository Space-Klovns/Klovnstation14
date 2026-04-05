using Content.Shared._KS14.McqDialogue;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.McqDialogue.UI;

[UsedImplicitly]
public sealed class McqDialogueBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private McqDialogueWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<McqDialogueWindow>();
    }

    /// <summary>
    /// Update the UI state based on server-sent info
    /// </summary>
    /// <param name="state"></param>
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (_window == null || state is not McqDialogueBoundUserInterfaceState dialogueState)
            return;

        foreach (var datum in dialogueState.DialogueData)
        {
            void Handler()
            {
                SendMessage(new McqDialogueDataSelectedMessage(datum.Id));
                Close();
            }

            _window.AddOption(datum.Text, Handler);
        }
    }
}
