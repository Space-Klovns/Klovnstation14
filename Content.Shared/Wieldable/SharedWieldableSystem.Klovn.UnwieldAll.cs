using Content.Shared.Hands.Components;
using Content.Shared.Wieldable.Components;

namespace Content.Shared.Wieldable;

public abstract partial class SharedWieldableSystem
{
    /// <summary>
    ///     KS14 - unwieldall but it returns a boolean if someone has been unwielded - required for a specific bit of demoncode
    /// </summary>
    /// <param name="force">If this is true we will bypass UnwieldAttemptEvent.</param>
    public bool TryUnwieldAll(Entity<HandsComponent?> wielderEntity, bool force = false)
    {
        var result = false;
        foreach (var heldUid in _hands.EnumerateHeld(wielderEntity))
        {
            if (TryComp<WieldableComponent>(heldUid, out var wieldableComponent))
                result |= TryUnwield((heldUid, wieldableComponent), wielderEntity, force);
        }

        return result;
    }
    // KS14 end
}
