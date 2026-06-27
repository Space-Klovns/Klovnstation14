using System.Numerics;
using Content.Shared._KS14.IoC;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Utility;

namespace Content.Client._KS14.ShadowOverlay;

public sealed class KsShadowOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly SystemCollectionHookManager _systemCollectionHookManager = default!;
    [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsShadowComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<KsShadowComponent, AppearanceChangeEvent>(OnAppearanceChanged);

        _systemCollectionHookManager.HookAction(OnDependenciesReady);
    }

    private void OnMapInit(Entity<KsShadowComponent> entity, ref MapInitEvent args)
    {
        if (!_appearanceSystem.TryGetData(entity.Owner, entity.Comp.Visuals, out var key) ||
            key.ToString() is not { } keyString ||
            keyString.Length == 0)
            return;

        entity.Comp.SpritesPerKey.TryGetValue(keyString, out var value);
        BuildVars(entity, value);
    }

    private void OnAppearanceChanged(Entity<KsShadowComponent> entity, ref AppearanceChangeEvent args)
    {
        if (!args.AppearanceData.TryGetValue(entity.Comp.Visuals, out var key) ||
            key.ToString() is not { } keyString ||
            keyString.Length == 0)
            return;

        entity.Comp.SpritesPerKey.TryGetValue(keyString, out var value);
        BuildVars(entity, value);
    }

    private static void BuildVars(KsShadowComponent component, PrototypeLayerData? data)
    {
        if (data is { })
        {
            component.Sprite = data.RsiPath is { } rsiPath &&
            data.State is { } rsiState ? new SpriteSpecifier.Rsi(new(rsiPath), rsiState) : new SpriteSpecifier.Texture(new(data.TexturePath!));

            component.Rotation = data.Rotation ?? Angle.Zero;
            component.Offset = data.Offset ?? Vector2.Zero;
            component.Modulate = data.Color ?? Color.White;

            return;
        }

        component.Sprite = null;
    }

    private void OnDependenciesReady(IDependencyCollection dependencyCollection)
    {
        var overlay = new KsShadowOverlay();

        dependencyCollection.InjectDependencies(overlay, oneOff: true);
        _overlayManager.AddOverlay(overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<KsShadowOverlay>();
    }
}
