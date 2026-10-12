using Content.Client._KS14.AdminMusic;
using Content.Client._KS14.TTS;
using Content.Client._KS14.Voice;

namespace Content.Client._KS14.IoC;

internal static class KsClientContentIoC
{
    public static void Register(IDependencyCollection dependencyCollection)
    {
        // Shouldnt call shared

        dependencyCollection.Register<KsAdminMusicManager>();
        dependencyCollection.Register<KsVoiceNetManager>();
        dependencyCollection.Register<KsTtsPreviewManager>();
    }
}
