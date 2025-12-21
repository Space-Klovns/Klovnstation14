// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.GameStates;
namespace Content.Shared._KS14.ArcFlash.Components;

/// <summary>
/// This component makes a building using it trigger an arc flash when deconstructed.
/// Rule of thumb, this has to be a machine buildable like other machines, or it has to be an APC (very niche)
/// It relies on construction graph nodes to raise events
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArcFlashDeconstructableComponent : Component
{
    [DataField, AutoNetworkedField]
    public float lightningRange = 4f;

    [DataField, AutoNetworkedField]
    public int lightningAmount = 1;

    [DataField, AutoNetworkedField]
    public string lightningPrototype = "ArcFlashLightningStrong";
}
