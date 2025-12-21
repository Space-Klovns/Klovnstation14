// SPDX-FileCopyrightText: 2024 Aidenkrz
// SPDX-FileCopyrightText: 2024 Piras314
// SPDX-FileCopyrightText: 2024 VMSolidus
// SPDX-FileCopyrightText: 2025 Aiden
// SPDX-FileCopyrightText: 2025 GoobBot
// SPDX-FileCopyrightText: 2025 Ilya246
// SPDX-FileCopyrightText: 2025 Misandry
// SPDX-FileCopyrightText: 2025 gus
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Prototypes;

namespace Content.Shared._Goobstation.Clothing.Components
{
    [RegisterComponent]
    public sealed partial class ClothingGrantComponentComponent : Component
    {
        [DataField("component", required: true)]
        [AlwaysPushInheritance]
        public ComponentRegistry Components { get; private set; } = new();

        [ViewVariables(VVAccess.ReadWrite)]
        public Dictionary<string, bool> Active = new(); // Goobstation
    }
}
