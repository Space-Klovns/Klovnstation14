using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Translation;

/// <summary>AHelp's » carries its own original, independent of ordinary chat history.</summary>
[UsedImplicitly]
public sealed class KsBwoinkTranslatedMarkerTag : IMarkupTagHandler
{
    public const string TagName = "ksbwoinktranslated";
    public string Name => TagName;

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (!node.Value.TryGetString(out var original))
            return false;

        control = new Label
        {
            Text = "» ",
            FontColorOverride = Color.FromHex("#6f9fd8"),
            MouseFilter = Control.MouseFilterMode.Stop,
            ToolTip = original,
        };
        return true;
    }
}
