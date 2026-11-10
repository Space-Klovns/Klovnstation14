using Content.Server.Chat.Systems;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     Makes a station-wide announcement on the sender's station.
/// </summary>
public sealed partial class KsLlmAnnouncementToolEffect : KsLlmToolEffect
{
    /// <summary>
    ///     Which argument holds the announcement text.
    /// </summary>
    [DataField]
    public string MessageArgument = "message";

    /// <summary>
    ///     Shown as the announcement's sender.
    /// </summary>
    [DataField]
    public LocId Sender = "ks-llm-announcement-sender";

    [DataField]
    public Color Color = Color.Gold;

    public override KsLlmToolOutcome Execute(in KsLlmToolContext context)
    {
        if (context.StationUid is not { } stationUid || !context.EntityManager.EntityExists(stationUid))
            return KsLlmToolOutcome.Error("the sending fax does not belong to any station.");

        // Announcements escape markup themselves; this only stops the model smuggling tags in as text.
        var message = FormattedMessage.RemoveMarkupPermissive(context.Arguments.GetString(MessageArgument) ?? string.Empty).Trim();
        if (message.Length == 0)
            return KsLlmToolOutcome.Error("the announcement was empty.");

        context.EntityManager.System<ChatSystem>().DispatchStationAnnouncement(stationUid,
            message,
            sender: context.Localization.GetString(Sender),
            colorOverride: Color);

        return KsLlmToolOutcome.Ok("The announcement was made.");
    }
}
