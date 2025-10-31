// SPDX-FileCopyrightText: 2021 Acruid
// SPDX-FileCopyrightText: 2021 Javier Guardia Fernández
// SPDX-FileCopyrightText: 2021 Paul
// SPDX-FileCopyrightText: 2021 Paul Ritter
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto
// SPDX-FileCopyrightText: 2022 Jezithyr
// SPDX-FileCopyrightText: 2022 mirrorcult
// SPDX-FileCopyrightText: 2022 wrexbe
// SPDX-FileCopyrightText: 2023 Chief-Engineer
// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 SlamBamActionman
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Body.Components;
using Content.Shared.Database;
using JetBrains.Annotations;

namespace Content.Server.Destructible.Thresholds.Behaviors
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class GibBehavior : IThresholdBehavior
    {
        [DataField("recursive")] private bool _recursive = true;

        public LogImpact Impact => LogImpact.Extreme;

        public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
        {
            if (system.EntityManager.TryGetComponent(owner, out BodyComponent? body))
            {
                system.BodySystem.GibBody(owner, _recursive, body, splatModifier: 8.5f);
            }
        }
    }
}
