using Content.Server._KS14.AdminMusic;
using Content.Server._KS14.AnnouncementWebhook;
using Content.Server._KS14.Antag;
using Content.Server._KS14.Llm;
using Content.Server._KS14.TTS;
using Content.Server._KS14.Voice;

namespace Content.Server._KS14.IoC;

internal static class KsServerContentIoC
{
    public static void Register(IDependencyCollection dependencyCollection)
    {
        // Shouldnt call shared

        dependencyCollection.Register<LastRolledAntagManager>();
        dependencyCollection.Register<AnnouncementWebhookManager>();
        dependencyCollection.Register<KsAdminMusicManager>();
        dependencyCollection.Register<KsLlmManager>();
        dependencyCollection.Register<KsVoiceLinkManager>();
        dependencyCollection.Register<KsVoiceUplinkManager>();
        dependencyCollection.Register<KsTtsPreviewManager>();
    }
}
