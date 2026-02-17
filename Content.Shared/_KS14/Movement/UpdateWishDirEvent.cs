// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;

namespace Content.Shared._KS14.Movement;

/// <summary>
///     Raised on a mover entity to update wish-dir if necessary.
/// </summary>
[ByRefEvent]
public record struct UpdateWishDirEvent(Vector2 WishDir);
