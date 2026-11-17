using Content.Shared._KS14.PopupLocalization;
using Content.Shared._Trauma.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Popups;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared._KS14.ThrowMode;

/// <summary>
///     Turns throw mode on and off, from its HUD button or its key, and throws when a client in throw mode clicks.
///     The client side, which turns the click into a <see cref="KsThrowModeThrowEvent"/>, is <c>KsThrowModeInputSystem</c>.
/// </summary>
public sealed partial class KsThrowModeSystem : EntitySystem
{
    [Dependency] private PredictedHandsSystem _predictedHandsSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeAllEvent<KsToggleThrowModeEvent>(OnToggleRequest);
        SubscribeAllEvent<KsThrowModeThrowEvent>(OnThrowRequest);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.KsToggleThrowMode, InputCmdHandler.FromDelegate(OnToggleKey, handle: false))
            .Register<KsThrowModeSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<KsThrowModeSystem>();
    }

    private void OnToggleKey(ICommonSession? session)
    {
        if (session?.AttachedEntity is { } player)
            Toggle(player);
    }

    private void OnToggleRequest(KsToggleThrowModeEvent args, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is { } player)
            Toggle(player);
    }

    private void Toggle(EntityUid player)
    {
        if (TryComp<KsThrowModeComponent>(player, out var throwMode))
            SetEnabled((player, throwMode), !throwMode.Enabled, popup: true);
    }

    private void OnThrowRequest(KsThrowModeThrowEvent args, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is not { } player || !TryComp<KsThrowModeComponent>(player, out var throwMode) ||
            !throwMode.Enabled)
        {
            return;
        }

        var coordinates = GetCoordinates(args.Coordinates);
        if (!coordinates.IsValid(EntityManager) || !_handsSystem.TryGetActiveItem(player, out _))
            return;

        // Throw mode stays on until it is toggled off: every click throws.
        _predictedHandsSystem.ThrowHeldItem(player, coordinates);
    }

    public bool IsEnabled(EntityUid uid)
    {
        return CompOrNull<KsThrowModeComponent>(uid)?.Enabled == true;
    }

    public void SetEnabled(Entity<KsThrowModeComponent> entity, bool enabled, bool popup)
    {
        if (entity.Comp.Enabled == enabled)
            return;

        entity.Comp.Enabled = enabled;
        Dirty(entity);

        if (popup)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(enabled ? "ks-throw-mode-enabled" : "ks-throw-mode-disabled"),
                entity, entity);
        }
    }
}
