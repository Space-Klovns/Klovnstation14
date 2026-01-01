// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MIT

using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Anomaly.Components;

/// <summary>
/// A tag component that allows an entity to be shoved into a disposal unit by the G.O.R.I.L.L.A. gauntlet.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GorillaDisposalComponent : Component;
