using Content.Shared._KS14.ArcFlash.Components;
using Content.Shared._KS14.Power;

namespace Content.Shared._KS14.ArcFlash;

public abstract class SharedArcFlashSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArcFlashCableComponent, AttemptCutCableEvent>(OnAttemptCutCable);
    }

    protected virtual void OnAttemptCutCable(Entity<ArcFlashCableComponent> entity, ref AttemptCutCableEvent args)
    {
        // oh my god doctos nooooooo
    }
}
