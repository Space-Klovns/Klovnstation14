using Content.Shared._KS14.Language;
using Content.Shared._KS14.Language.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Language;

public sealed partial class KsLanguageSystem : EntitySystem
{
    public event Action? LanguagesUpdated;

    [SubscribeLocalEvent]
    private void OnSpeakerState(EntityUid uid, KsLanguageSpeakerComponent component, ref AfterAutoHandleStateEvent args)
    {
        LanguagesUpdated?.Invoke();
    }

    // Broadcast: the new body may lack a speaker component and the UI must still drop the
    // old roster.
    [SubscribeLocalEvent]
    private void OnPlayerAttachedChanged(LocalPlayerAttachedEvent ev)
    {
        LanguagesUpdated?.Invoke();
    }

    [SubscribeLocalEvent]
    private void OnPlayerDetachedChanged(LocalPlayerDetachedEvent ev)
    {
        LanguagesUpdated?.Invoke();
    }

    public void RequestSetLanguage(ProtoId<KsLanguagePrototype> language)
    {
        RaiseNetworkEvent(new KsSetLanguageMessage { Language = language });
    }
}
