using Content.Shared._KS14.BatteryShielding;

namespace Content.Client._KS14.BatteryShielding;

public sealed partial class BatteryShieldingSystem : SharedBatteryShieldingSystem
{
    [Dependency] private SharedUserInterfaceSystem _userInterfaceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BatteryShieldingComponent, AfterAutoHandleStateEvent>(OnAfterAutoHandleState);
    }

    private void OnAfterAutoHandleState(Entity<BatteryShieldingComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateUi(ent);
    }

    protected override void UpdateUi(Entity<BatteryShieldingComponent> entity)
    {
        if (entity.Comp.UiKey is not { } uiKey ||
            !_userInterfaceSystem.TryGetOpenUi(entity.Owner, uiKey, out var bui))
            return;

        bui.Update();
    }
}
