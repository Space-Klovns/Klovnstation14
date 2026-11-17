using Content.Client.Lathe.UI;
using Content.Shared._KS14.SalvageTablet;
using Content.Shared.Lathe;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.SalvageTablet;

/// <summary>
///     Refreshes an open smelter window when the points it holds change.
/// </summary>
public sealed partial class KsMiningPointsSmelterUiSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;

    [SubscribeLocalEvent]
    private void OnSmelterState(Entity<KsMiningPointsSmelterComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        if (_userInterfaceSystem.TryGetOpenUi<LatheBoundUserInterface>(entity.Owner, LatheUiKey.Key, out var boundUserInterface))
            boundUserInterface.Update();
    }
}
