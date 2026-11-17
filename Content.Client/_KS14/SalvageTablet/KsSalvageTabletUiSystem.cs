using Content.Shared._KS14.SalvageTablet;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.SalvageTablet;

/// <summary>
///     Redraws an open tablet when its networked component changes, which no BUI state announces.
/// </summary>
public sealed partial class KsSalvageTabletUiSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;

    [SubscribeLocalEvent]
    private void OnTabletState(Entity<KsSalvageTabletComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        if (_userInterfaceSystem.TryGetOpenUi<KsSalvageTabletBoundUserInterface>(entity.Owner, KsSalvageTabletUiKey.Key, out var boundUserInterface))
            boundUserInterface.Update();
    }
}
