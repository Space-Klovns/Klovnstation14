// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.Speczones;

namespace Content.Client._KS14.Speczones;

/// <inheritdoc/>
public sealed class SpeczoneSystem : SharedSpeczoneSystem
{
    protected override bool HasSpeczoneComponent(EntityUid uid) => HasComp<SpeczoneComponent>(uid);
}
