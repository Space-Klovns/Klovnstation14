using Content.Shared.Popups;

namespace Content.Client.Popups;

// A popup the client predicted is dropped when the server's copy of it arrives, and stock, the two are told to be the
// same by their tick. That holds for anything done on an input, which both run on the same tick. It doesn't for what
// physics decides, such as a thrown body hitting something: the client has the hit a tick or two off the server's,
// and saw the popup twice. So a server popup with no predicted twin on its own tick also takes one from a nearby tick.
public sealed partial class PopupSystem
{
    /// <summary>
    ///     How far apart, in seconds, a predicted entity popup and the server's copy of it may be and still be one.
    /// </summary>
    private const float KsPredictionMatchSeconds = 0.5f;

    /// <summary>
    ///     Drops the predicted popup nearest in time to a server popup that says the same on the same entity, if one
    ///         is near enough.
    /// </summary>
    /// <returns>Whether one was dropped: the server's popup is then not shown again.</returns>
    private bool KsTryRemoveNearbyPrediction(PopupEntityEvent ev)
    {
        var maximumTicks = (int)(KsPredictionMatchSeconds * Timing.TickRate);
        var nearestIndex = -1;
        var nearestTicks = maximumTicks + 1;

        for (var index = 0; index < _predictionInstances.Count; index++)
        {
            if (_predictionInstances[index] is not PopupEntityEvent.PredictionInstance predicted ||
                predicted.Uid != ev.Uid || predicted.Type != ev.Type || predicted.Message != ev.Message)
            {
                continue;
            }

            var ticksApart = Math.Abs((long)predicted.Tick.Value - ev.Tick.Value);
            if (ticksApart >= nearestTicks)
                continue;

            nearestTicks = (int)ticksApart;
            nearestIndex = index;
        }

        if (nearestIndex < 0)
            return false;

        _predictionInstances.RemoveAt(nearestIndex);
        return true;
    }
}
