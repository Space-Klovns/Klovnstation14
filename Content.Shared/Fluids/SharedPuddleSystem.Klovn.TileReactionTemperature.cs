namespace Content.Shared.Fluids;

public abstract partial class SharedPuddleSystem
{
    /// <summary>
    ///     The temperature, in kelvin, of the liquid whose reagents are currently being run through
    ///         <see cref="Content.Shared.Chemistry.Reagent.ReagentPrototype.ReactionTile"/>, or null when no caller
    ///         said. A tile reaction that creates new liquid out of the reacting reagent (spilling it into a puddle) reads
    ///         this so the new liquid carries the source's heat instead of an arbitrary default. Set it only through
    ///         <see cref="KsBeginTileReactionTemperature"/>.
    /// </summary>
    public float? KsTileReactionSourceTemperature { get; private set; }

    /// <summary>
    ///     Declares the temperature of the liquid that tile reactions are about to be run for. Dispose the result
    ///         (<c>using var</c>) when the reactions are done; the previous value is restored then, so scopes nest.
    /// </summary>
    public KsTileReactionTemperatureScope KsBeginTileReactionTemperature(float temperature)
    {
        var scope = new KsTileReactionTemperatureScope(this, KsTileReactionSourceTemperature);
        KsTileReactionSourceTemperature = temperature;
        return scope;
    }

    public readonly struct KsTileReactionTemperatureScope : IDisposable
    {
        private readonly SharedPuddleSystem _system;
        private readonly float? _previous;

        internal KsTileReactionTemperatureScope(SharedPuddleSystem system, float? previous)
        {
            _system = system;
            _previous = previous;
        }

        public void Dispose()
        {
            _system.KsTileReactionSourceTemperature = _previous;
        }
    }
}
