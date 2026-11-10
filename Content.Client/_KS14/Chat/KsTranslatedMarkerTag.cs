using System.Diagnostics.CodeAnalysis;
using Content.Client.UserInterface.Systems.Chat;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Chat;

/// <summary>
///     Inline chat marker injected in front of a line that has been swapped for a translation. Hovering it
///     reveals the original (pre-translation) text, looked up from <see cref="ChatUIController"/> by the
///     message id carried in the tag's "id" attribute, e.g. "[kstranslated id=42]".
/// </summary>
[UsedImplicitly]
public sealed partial class KsTranslatedMarkerTag : IMarkupTagHandler
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    public const string TagName = "kstranslated";
    public const string IdParam = "id";
    public const string OriginalParam = "original"; // BS: UPSTREAM-BS-TRANSLATION-001, historical rows outlive server-local IDs.

    public string Name => TagName;

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        // BS: Retained history stores the original in the tag so reconnects cannot change its tooltip.
        if (!node.Attributes.TryGetValue(OriginalParam, out var frozen) || !frozen.TryGetString(out var original))
        {
            if (!node.Attributes.TryGetValue(IdParam, out var idParam) || !idParam.TryGetLong(out var id))
                return false;

            _ui.GetUIController<ChatUIController>().TryGetTranslationOriginal((int) id.Value, out original);
        }

        var label = new Label
        {
            Text = "» ",
            FontColorOverride = Color.FromHex("#6f9fd8"),
            MouseFilter = Control.MouseFilterMode.Stop, // required so the tooltip fires on hover
            ToolTip = original, // BS: Both current and retained messages use their own original.
        };

        control = label;
        return true;
    }
}
