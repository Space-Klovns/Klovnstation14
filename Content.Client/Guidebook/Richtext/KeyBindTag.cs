using JetBrains.Annotations;
using Robust.Client.Input;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client.Guidebook.Richtext;

[UsedImplicitly]
public sealed partial class KeyBindTag : IMarkupTagHandler
{
    [Dependency] private IInputManager _inputManager = default!;

    public string Name => "keybind";

    public string TextBefore(MarkupNode node)
    {
        if (!node.Value.TryGetString(out var keyBindName))
            return "";

        if (!_inputManager.TryGetKeyBinding(keyBindName, out var binding))
        {
            // KS14 start: an unbound key shows the name it has in the controls options, not its internal function name
            if (Loc.TryGetString($"ui-options-function-{CaseConversion.PascalToKebab(keyBindName)}", out var functionName))
                return functionName;
            // KS14 end

            return keyBindName;
        }

        return binding.GetKeyString();
    }
}
