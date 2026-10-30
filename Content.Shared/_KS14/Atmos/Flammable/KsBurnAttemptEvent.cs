namespace Content.Shared._KS14.Atmos.Flammable;

/// <summary>
///     Raised on a flammable entity when it is about to catch fire, and on each of the flammable system's updates
///         while it burns. Cancelling it keeps the entity from catching fire, or puts it out.
/// </summary>
/// <remarks>
///     Raised by the server's <c>FlammableSystem</c>, which is the only side that sets things on fire.
///     Only for things that can be put out (<c>FlammableComponent.CanExtinguish</c>) is the "keeps burning" half of
///         any use, since extinguishing does nothing to the others.
/// </remarks>
[ByRefEvent]
public record struct KsBurnAttemptEvent
{
    public bool Cancelled { get; private set; }

    public void Cancel() => Cancelled = true;
}
