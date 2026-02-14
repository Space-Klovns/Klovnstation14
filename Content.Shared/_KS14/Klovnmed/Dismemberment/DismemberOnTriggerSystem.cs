// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Trigger;

namespace Content.Shared._KS14.Klovnmed.Dismemberment;

public sealed class DismemberOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly DismembermentSystem _dismembermentSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DismemberOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<DismemberOnTriggerComponent> entity, ref TriggerEvent args)
    {
        if ((entity.Comp.TargetUser ? args.User : entity.Owner) is not { } targetUid)
            return;

        // TODO LCDC: on upstream merge handle TriggerEvent.Predicted
        args.Handled |= _dismembermentSystem.TryDismemberRandomBodyPartOfType(
            targetUid,
            entity.Comp.PartType,
            out _,
            throwSpeed: entity.Comp.ThrowSpeed
        );
    }
}
