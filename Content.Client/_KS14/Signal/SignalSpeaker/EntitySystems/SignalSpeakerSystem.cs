using Content.Client._KS14.Signal.SignalSpeaker.UI;
using Content.Shared._KS14.Signal.SignalSpeaker;
using Content.Shared._KS14.Signal.SignalSpeaker.Components;
using Content.Shared._KS14.Signal.SignalSpeaker.EntitySystems;

namespace Content.Client._KS14.Signal.SignalSpeaker.EntitySystems;

public sealed class SignalSpeakerSystem : SharedSignalSpeakerSystem
{
    protected override void UpdateUI(Entity<SignalSpeakerComponent> ent)
    {
        if (UserInterfaceSystem.TryGetOpenUi(ent.Owner, SignalSpeakerUiKey.Key, out var bui)
            && bui is SignalSpeakerBoundUserInterface cBui)
        {
            cBui.Reload();
        }
    }
}
