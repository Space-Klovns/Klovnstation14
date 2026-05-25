using Content.Server._KS14.AnnouncementWebhook;
using Content.Server._KS14.Antag;

namespace Content.Server._KS14.IoC;

internal static class KsServerContentIoC
{
    public static void Register(IDependencyCollection dependencyCollection)
    {
        // Shouldnt call shared

        dependencyCollection.Register<LastRolledAntagManager>();
        dependencyCollection.Register<AnnouncementWebhookManager>();
    }
}
