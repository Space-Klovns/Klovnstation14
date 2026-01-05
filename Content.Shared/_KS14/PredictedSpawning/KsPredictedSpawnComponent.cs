// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.GameStates;

namespace Content.Shared._KS14.PredictedSpawning;

/// <summary>
///     When added on client, deletes the entity it was added to.
/// </summary>
[RegisterComponent, NetworkedComponent]
[UnsavedComponent]
public sealed partial class KsPredictedSpawnComponent : Component;
