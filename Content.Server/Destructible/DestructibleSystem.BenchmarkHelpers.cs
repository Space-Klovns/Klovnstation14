// SPDX-FileCopyrightText: 2025 Hannah Giovanna Dawson
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 Princess Cheeseballs
// SPDX-FileCopyrightText: 2025 nabegator220
// SPDX-FileCopyrightText: 2026 deltanedas
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Content.Shared.Destructible; // Trauma

namespace Content.Server.Destructible;

public sealed partial class DestructibleSystem
{
    /// <summary>
    /// Tests all triggers in a DestructibleComponent to see how expensive it is to query them.
    /// </summary>
    public void TestAllTriggers(List<Entity<Shared.Damage.Components.DamageableComponent, DestructibleComponent>> destructibles)
    {
        foreach (var (uid, damageable, destructible) in destructibles)
        {
            foreach (var threshold in destructible.Thresholds)
            {
                // Chances are, none of these triggers will pass!
                Triggered(threshold, (uid, damageable));
            }
        }
    }

    /// <summary>
    /// Tests all behaviours in a DestructibleComponent to see how expensive it is to query them.
    /// </summary>
    public void TestAllBehaviors(List<Entity<Shared.Damage.Components.DamageableComponent, DestructibleComponent>> destructibles)
    {
       foreach (var (uid, damageable, destructible) in destructibles)
       {
           foreach (var threshold in destructible.Thresholds)
           {
               Execute(threshold, uid);
           }
       }
    }
}
