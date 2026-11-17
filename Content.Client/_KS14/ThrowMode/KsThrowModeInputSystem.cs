using Content.Client.Interaction;
using Content.Shared._KS14.ThrowMode;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._KS14.ThrowMode;

/// <summary>
///     In throw mode, a click throws the active hand's item rather than using it. The click is swallowed here and
///         sent as a predicted <see cref="KsThrowModeThrowEvent"/>, so neither side runs it as an interaction.
/// </summary>
public sealed partial class KsThrowModeInputSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private KsThrowModeSystem _throwModeSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .BindBefore(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnUse, outsidePrediction: true),
                typeof(SharedInteractionSystem), typeof(DragDropSystem))
            .Register<KsThrowModeInputSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<KsThrowModeInputSystem>();
    }

    private bool OnUse(ICommonSession? session, EntityCoordinates coordinates, EntityUid uid)
    {
        if (_playerManager.LocalEntity is not { } player || !_throwModeSystem.IsEnabled(player) ||
            !_handsSystem.TryGetActiveItem(player, out _) || !coordinates.IsValid(EntityManager))
        {
            return false;
        }

        RaisePredictiveEvent(new KsThrowModeThrowEvent(GetNetCoordinates(coordinates)));
        return true;
    }
}
