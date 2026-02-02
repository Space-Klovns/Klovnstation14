// SPDX-FileCopyrightText: 2026 nabegator220
//
// SPDX-License-Identifier: MPL-2.0

namespace Content.Shared._KS14.Held.Components; // KS14 - this is an analogue to the goobstation clothinggrantsystem that we already ported
[RegisterComponent]
public sealed partial class HeldGrantTagComponent : Component
{
    [DataField("tag", required: true), ViewVariables(VVAccess.ReadWrite)]
    public string Tag = "";

    [ViewVariables(VVAccess.ReadWrite)]
    public bool IsActive = false;
}
