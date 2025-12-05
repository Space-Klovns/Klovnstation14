// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.GameStates;
namespace Content.Shared._KS14.ArcFlash.Components;

/// <summary>
/// This component makes a building using it trigger an arc flash when unanchored and powered (substations, SMESes)
/// REQUIRES AN ELECTRIFIEDCOMPONENT ALONGSIDE IT TO FUNCTION!
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ArcFlashAnchorableComponent : Component
{
}
