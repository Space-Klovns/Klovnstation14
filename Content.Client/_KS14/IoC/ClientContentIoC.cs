using Content.Client._KS14.AdminMusic;

namespace Content.Client._KS14.IoC;

internal static class KsClientContentIoC
{
    public static void Register(IDependencyCollection dependencyCollection)
    {
        // Shouldnt call shared

        dependencyCollection.Register<KsAdminMusicManager>();
    }
}
