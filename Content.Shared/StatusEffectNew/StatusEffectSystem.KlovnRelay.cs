using Content.Shared.Gravity;
using Content.Shared.StatusEffectNew.Components;

namespace Content.Shared.StatusEffectNew;

public sealed partial class StatusEffectsSystem
{
    private void KsInitializeRelay()
    {
        SubscribeLocalEvent<StatusEffectContainerComponent, IsWeightlessEvent>(RefRelayStatusEffectEvent);
    }
}
