// SPDX-FileCopyrightText: 2026 nabegator220
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Held.Components // KS14 - this is an analogue to the goobstation clothinggrantsystem that we already ported
{
    [RegisterComponent]
    public sealed partial class HeldGrantComponentComponent : Component
    {
        [DataField("component", required: true)]
        [AlwaysPushInheritance]
        public ComponentRegistry Components { get; private set; } = new();

        [ViewVariables(VVAccess.ReadWrite)]
        public Dictionary<string, bool> Active = new();
    }
}
