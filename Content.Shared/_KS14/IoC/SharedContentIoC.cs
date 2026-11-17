using Content.Shared._KS14.RadioLocalization;

namespace Content.Shared._KS14.IoC;

public static class KsSharedContentIoC
{
    public static void Register(IDependencyCollection dependencyCollection)
    {
        dependencyCollection.Register<SystemCollectionHookManager>();
        dependencyCollection.Register<KsRadioLocalization>();
    }
}
