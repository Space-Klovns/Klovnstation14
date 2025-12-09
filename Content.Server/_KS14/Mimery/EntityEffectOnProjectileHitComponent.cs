// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MIT

using Content.Shared.EntityEffects;

namespace Content.Server._KS14.Mimery;

[RegisterComponent]
public sealed partial class EntityEffectOnProjectileHitComponent : Component
{
    [DataField]
    public List<EntityEffect> Effects = new();
}
