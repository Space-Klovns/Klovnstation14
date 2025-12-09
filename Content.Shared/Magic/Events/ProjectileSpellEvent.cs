// SPDX-FileCopyrightText: 2022 Kara
// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2023 Leon Friedrich
// SPDX-FileCopyrightText: 2024 keronshb
// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 J
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MIT

using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared.Magic.Events;

[Virtual]
public partial class ProjectileSpellEvent : WorldTargetActionEvent
{
    /// <summary>
    /// What entity should be spawned.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Prototype;
}
