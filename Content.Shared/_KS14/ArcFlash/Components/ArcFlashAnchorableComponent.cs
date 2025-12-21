// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.GameStates;
namespace Content.Shared._KS14.ArcFlash.Components;

/// <summary>
/// This component makes a building using it trigger an arc flash when unanchored and on powered hv wire (substations, SMESes)
/// REQUIRES AN ELECTRIFIEDCOMPONENT ALONGSIDE IT TO FUNCTION!
/// usually results in the damage of the building alongside the person who did it - substations can to blow up
/// this can be rectified in code if it proves to be a problem in the gameplay
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArcFlashAnchorableComponent : Component
{
    [DataField, AutoNetworkedField]
    public float lightningRange = 3f;

    [DataField, AutoNetworkedField]
    public int lightningAmount = 1;

    [DataField, AutoNetworkedField]
    public string lightningPrototype = "ArcFlashLightningWeak";
}
