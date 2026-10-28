using Content.Shared._KS14.McqDialogue;

namespace Content.Client._KS14.McqDialogue.UI;

public sealed partial class McqDialogueBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables] private McqDialogueWindow? _window;

    /// <summary>
    ///     Update the UI state based on server-sent info. The window is made with the first state rather than on open,
    ///         since the state says which source it's about, and so which window the client may already have open.
    /// </summary>
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not McqDialogueBoundUserInterfaceState dialogueState)
            return;

        if (_window == null && !TryCreateWindow(dialogueState))
            return;

        _window!.SetHeader(dialogueState.Title, dialogueState.Prompt);

        foreach (var datum in dialogueState.DialogueData)
        {
            void Handler()
            {
                SendMessage(new McqDialogueDataSelectedMessage(datum.Id));
                Close();
            }

            _window.AddOption(datum, Handler);
        }
    }

    /// <returns>False if the dialogue was already answered or closed while the client waited for it.</returns>
    private bool TryCreateWindow(McqDialogueBoundUserInterfaceState dialogueState)
    {
        var predictionSystem = EntMan.System<McqDialoguePredictionSystem>();
        if (predictionSystem.TryTakeOver(EntMan.GetEntity(dialogueState.Source), out var predictedWindow, out var chosenId))
        {
            // Told to the server rather than closed here: this runs inside the engine's loop opening queued UIs, which
            //      closing would add to. Either way the server closes the dialogue, and this UI with it.
            if (predictedWindow == null)
            {
                SendMessage(chosenId != null ? new McqDialogueDataSelectedMessage(chosenId) : new CloseBoundInterfaceMessage());
                return false;
            }

            // Its buttons answered the prediction; these answer the dialogue.
            predictedWindow.ClearOptions();
            _window = predictedWindow;
        }
        else
        {
            _window = new McqDialogueWindow();
            _window.OpenCentered();
        }

        _window.OnClose += Close;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _window?.Close();
        _window?.Dispose();
        _window = null;
    }
}
