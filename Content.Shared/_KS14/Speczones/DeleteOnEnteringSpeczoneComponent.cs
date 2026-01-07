// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Speczones;

/// <summary>
///     Deletes this entity when it enters a speczone, or
///         when this component gets started on it while it's
///         in a speczone.
/// </summary>
[NetworkedComponent]
[RegisterComponent]
public sealed partial class RelocateOnEnteringSpeczoneComponent : Component;
