// SPDX-License-Identifier: MPL-2.0

using Content.Server._KS14.Antag;

namespace Content.Server._KS14.IoC;

internal static class KsServerContentIoC
{
    public static void Register(IDependencyCollection dependencyCollection)
    {
        // Add KsSharedContentIoC here if we ever need it.

        dependencyCollection.Register<LastRolledAntagManager>();
    }
}
