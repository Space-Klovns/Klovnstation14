// SPDX-FileCopyrightText: 2021 Acruid
// SPDX-FileCopyrightText: 2021 Javier Guardia Fernández
// SPDX-FileCopyrightText: 2021 Paul
// SPDX-FileCopyrightText: 2021 Paul Ritter
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto
// SPDX-FileCopyrightText: 2022 Leon Friedrich
// SPDX-FileCopyrightText: 2022 Moony
// SPDX-FileCopyrightText: 2022 mirrorcult
// SPDX-FileCopyrightText: 2022 wrexbe
// SPDX-FileCopyrightText: 2023 Chief-Engineer
// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2026 deltanedas
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Explosion.Components;
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Behaviors;
using JetBrains.Annotations;

namespace Content.Server.Destructible.Thresholds.Behaviors
{
    /// <summary>
    ///     This behavior will trigger entities with <see cref="ExplosiveComponent"/> to go boom.
    /// </summary>
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class ExplodeBehavior : IThresholdBehavior
    {
        public void Execute(EntityUid owner, SharedDestructibleSystem system, EntityUid? cause = null)
        {
            system.ExplosionSystem.TriggerExplosive(owner, user:cause);
        }
    }
}
