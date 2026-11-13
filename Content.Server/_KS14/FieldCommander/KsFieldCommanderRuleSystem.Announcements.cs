using System.Numerics;
using Robust.Shared.Random;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Announcements

    /// <summary>
    ///     One moment on both sides' channels: the commander's taunt, if it has one, at once; then the defenders'
    ///         intelligence on it, if there is any, as a reply a few seconds later. The intelligence comes with its
    ///         chirp, unless the commander's march has just played for a major taunt, so that the two don't pile up.
    /// </summary>
    /// <param name="major">Whether the taunt is for a moment that matters, and so comes with the march.</param>
    /// <param name="intelArguments">For the intelligence: the commander never gives anything away.</param>
    private void Transmit(KsFieldCommanderRuleComponent component, LocId? taunt, bool major, LocId? intel, params (string, object)[] intelArguments)
    {
        if (taunt is { } tauntId)
        {
            _chatSystem.DispatchGlobalAnnouncement(Loc.GetString(tauntId),
                Loc.GetString(component.Sender),
                playSound: major,
                announcementSound: major ? component.MajorSound : null,
                colorOverride: component.AnnouncementColor);
        }

        if (intel is { } intelId)
        {
            var sound = !(major && taunt != null);
            component.PendingIntel.Add(new KsFieldCommanderPendingIntel(_gameTiming.CurTime + component.IntelDelay, Loc.GetString(intelId, intelArguments), sound));
        }
    }

    /// <summary>
    ///     Sends whatever intelligence is due. The text was formatted when it was queued, from the state it describes.
    /// </summary>
    private void SendDueIntel(KsFieldCommanderRuleComponent component, TimeSpan curTime)
    {
        for (var i = 0; i < component.PendingIntel.Count; i++)
        {
            var intel = component.PendingIntel[i];
            if (intel.At > curTime)
                continue;

            SendIntel(component, intel.Message, intel.Sound);
            component.PendingIntel.RemoveAt(i);
            i--;
        }
    }

    private void SendIntel(KsFieldCommanderRuleComponent component, string message, bool sound)
    {
        _chatSystem.DispatchGlobalAnnouncement(message,
            Loc.GetString(component.IntelSender),
            playSound: sound,
            announcementSound: sound ? component.IntelSound : null,
            colorOverride: component.IntelColor);
    }

    private LocId? PickRandom(List<LocId> messageIds)
    {
        return messageIds.Count > 0 ? _robustRandom.Pick(messageIds) : (LocId?)null;
    }

    /// <summary>
    ///     A compass direction for an offset from the defenders' base, for intelligence to give bearings in.
    /// </summary>
    private static string GetCompassDirection(Vector2 offset)
    {
        return offset.GetDir().ToString().ToLowerInvariant();
    }

    #endregion
}
