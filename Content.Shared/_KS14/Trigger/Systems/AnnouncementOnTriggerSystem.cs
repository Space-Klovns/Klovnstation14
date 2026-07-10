using Content.Shared._KS14.Trigger.Components;
using Content.Shared.Chat;
using Content.Shared.Trigger;

namespace Content.Shared._KS14.Trigger.Systems;

public sealed partial class AnnouncementOnTriggerSystem : EntitySystem
{
    [Dependency] private SharedChatSystem _chatSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AnnouncementOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<AnnouncementOnTriggerComponent> entity, ref TriggerEvent args)
    {
        if (args.Key is not { } key ||
            !entity.Comp.AnnouncementsPerKey.TryGetValue(key, out var announcementDatum))
            return;

        var sender = announcementDatum.SenderLoc is { } senderLoc ?
            Loc.GetString(senderLoc) :
            null;

        _chatSystem.DispatchGlobalAnnouncement(
            Loc.GetString(announcementDatum.AnnouncementLoc),
            sender,
            playSound: announcementDatum.Sound is { },
            announcementSound: announcementDatum.Sound,
            colorOverride: announcementDatum.ColorOverride
        );

        args.Handled = true;
    }
}
