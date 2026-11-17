using Content.Shared.Chat;
using Content.Shared.Roles.RoleCodeword;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    // UPSTREAM-BS-TRANSLATION-001: reuse normal decoration after rebuilding a hidden translation from its template.
    private void KsApplyChatHighlights(ChatMessage msg)
    {
        // Color any words chosen by the client.
        foreach (var highlight in _highlights)
        {
            msg.WrappedMessage = SharedChatSystem.InjectTagAroundString(msg, highlight, "color", _highlightsColor);
        }

        // Color any codewords for minds that have roles that use them
        if (_player.LocalUser != null && _mindSystem != null && _roleCodewordSystem != null)
        {
            if (_mindSystem.TryGetMind(_player.LocalUser.Value, out var mindId) && _ent.TryGetComponent(mindId, out RoleCodewordComponent? codewordComp))
            {
                foreach (var (_, codewordData) in codewordComp.RoleCodewords)
                {
                    foreach (string codeword in codewordData.Codewords)
                        msg.WrappedMessage = SharedChatSystem.InjectTagAroundString(msg, codeword, "color", codewordData.Color.ToHex());
                }
            }
        }
    }
}
