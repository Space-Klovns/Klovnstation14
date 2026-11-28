namespace Content.Shared._KS14.Cultivation;

/// <summary>
///     A technique's action event that costs qi. <see cref="Systems.CultivationActionPredictionSystem"/> refuses it on
///         the client straight away when the cultivator can't pay.
/// </summary>
public interface IQiCostEvent
{
    float QiCost { get; }
}
