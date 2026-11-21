using Content.Client._KS14.Cultivation.UI;
using Content.Shared._KS14.Cultivation.Components;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Cultivation;

/// <summary>
///     Redraws an open Techniques menu when the loadout changes, so it can stay open while techniques are toggled.
/// </summary>
public sealed partial class CultivationLoadoutUiSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _uiSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CultivationLoadoutComponent, AfterAutoHandleStateEvent>(OnLoadoutState);
    }

    private void OnLoadoutState(Entity<CultivationLoadoutComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        if (_uiSystem.TryGetOpenUi<TechniqueLoadoutBoundUserInterface>(entity.Owner, TechniqueLoadoutUiKey.Key, out var bui))
            bui.Update();
    }
}
