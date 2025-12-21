// SPDX-FileCopyrightText: 2024 Aidenkrz
// SPDX-FileCopyrightText: 2024 Piras314
// SPDX-FileCopyrightText: 2024 VMSolidus
// SPDX-FileCopyrightText: 2025 Aiden
// SPDX-FileCopyrightText: 2025 Misandry
// SPDX-FileCopyrightText: 2025 gus
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Goobstation.Clothing.Components;

[RegisterComponent]
public sealed partial class ClothingGrantTagComponent : Component
{
    [DataField("tag", required: true), ViewVariables(VVAccess.ReadWrite)]
    public string Tag = "";

    [ViewVariables(VVAccess.ReadWrite)]
    public bool IsActive = false;
}
