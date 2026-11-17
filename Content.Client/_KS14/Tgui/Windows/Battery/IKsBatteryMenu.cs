using Content.Shared.Power;

namespace Content.Client._KS14.Tgui.Windows.Battery;

/// <summary>
///     What the battery's bound user interface needs of its window, so that the stock menu and the tgui one are
///         interchangeable.
/// </summary>
public interface IKsBatteryMenu : IDisposable
{
    event Action<bool>? OnInBreaker;
    event Action<bool>? OnOutBreaker;
    event Action<float>? OnChargeRate;
    event Action<float>? OnDischargeRate;

    void SetEntity(EntityUid entity);

    void Update(BatteryBuiState state);
}
