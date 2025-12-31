// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Execution;

/// <summary>
/// Used to whitelist guns that can be used for executions.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GunExecutionWhitelistComponent : Component;
