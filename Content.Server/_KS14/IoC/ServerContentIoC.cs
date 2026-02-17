// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Server._KS14.AnnouncementWebhook;
using Content.Server._KS14.Antag;

namespace Content.Server._KS14.IoC;

internal static class KsServerContentIoC
{
    public static void Register(IDependencyCollection dependencyCollection)
    {
        // Add KsSharedContentIoC here if we ever need it.

        dependencyCollection.Register<LastRolledAntagManager>();
        dependencyCollection.Register<AnnouncementWebhookManager>();
    }
}
