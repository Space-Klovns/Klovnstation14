using Robust.Shared.Player;

namespace Content.Shared.Effects;

public abstract partial class SharedColorFlashEffectSystem : EntitySystem
{
    public abstract void RaiseEffect(Color color, List<EntityUid> entities, Filter filter);
}
