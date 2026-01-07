// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Behaviors;
using JetBrains.Annotations;

namespace Content.Server._FarHorizons.Power.Generation.FissionGenerator;

/// <summary>
///     Calls <see cref="TurbineSystem.TearApart"/> on the entity that
///         this behaviour is directed at.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class TurbineBladeDestructionBehaviour : IThresholdBehavior
{
    private TurbineSystem? _turbineSystem = null;

    public void Execute(EntityUid owner, SharedDestructibleSystem system, EntityUid? cause = null)
    {
        _turbineSystem ??= system.EntityManager.System<TurbineSystem>();
        _turbineSystem.TearApart(owner, cause: cause);
    }
}
