// SPDX-FileCopyrightText: 2025 jhrushbe
//
// SPDX-License-Identifier: MPL-2.0

using Content.Client.Examine;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._FarHorizons.Power.Generation.FissionGenerator;

public sealed class TurbineSystem : SharedTurbineSystem
{
    [Dependency] private readonly UserInterfaceSystem _userInterfaceSystem = default!;

    private static readonly EntProtoId ArrowPrototype = "TurbineFlowArrow";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TurbineComponent, ClientExaminedEvent>(OnClientExamined);
    }

    private void OnClientExamined(Entity<TurbineComponent> entity, ref ClientExaminedEvent _) => Spawn(ArrowPrototype, new EntityCoordinates(entity.Owner, 0f, 0f));

    protected override void UpdateUi(Entity<TurbineComponent> entity)
    {
        if (_userInterfaceSystem.TryGetOpenUi(entity.Owner, TurbineUiKey.Key, out var bui))
        {
            bui.Update();
        }
    }
}
