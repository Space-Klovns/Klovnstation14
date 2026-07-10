using Content.Shared.Storage;
using Content.Shared.Lock;
using Robust.Client.GameObjects;

namespace Content.Client.Lock.Visualizers;

public sealed partial class LockVisualizerSystem : VisualizerSystem<LockVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, LockVisualsComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null
            || !AppearanceSystem.TryGetData<bool>(uid, LockVisuals.Locked, out _, args.Component))
            return;

        // Lock state for the entity.
        if (!AppearanceSystem.TryGetData<bool>(uid, LockVisuals.Locked, out var locked, args.Component))
            locked = true;

        // KS14 Start: find the layer
        if (!SpriteSystem.TryGetLayer((uid, args.Sprite), LockVisualLayers.Lock, out var lockLayer, logMissing: true))
            return;
        // KS14 End

        var unlockedStateExist = (lockLayer.RSI ?? args.Sprite.BaseRSI)?/* KS14: use layer RSI, fallback to sprite RSI instead of only using sprite RSI */.TryGetState(comp.StateUnlocked, out _);

        if (AppearanceSystem.TryGetData<bool>(uid, StorageVisuals.Open, out var open, args.Component))
        {
            SpriteSystem.LayerSetVisible(lockLayer /* KS14: directly specify the layer */, !open);
        }
        else if (!(bool)unlockedStateExist!)
            SpriteSystem.LayerSetVisible(lockLayer /* KS14: directly specify the layer */, locked);

        if (!open && (bool)unlockedStateExist!)
        {
            SpriteSystem.LayerSetRsiState(lockLayer /* KS14: directly specify the layer */, locked ? comp.StateLocked : comp.StateUnlocked);
        }
    }
}

public enum LockVisualLayers : byte
{
    Lock
}
