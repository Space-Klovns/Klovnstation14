using Robust.Shared.Map;

namespace Content.Shared.Chemistry.Components // KS14: moved from Content.Server, so the shooter's client predicts its own vapor (SharedVaporSystem)
{
    [RegisterComponent]
    public sealed partial class VaporComponent : Component
    {
        /// <summary>
        /// Stores data on the previously reacted tile. We only want to do reaction checks once per tile.
        /// </summary>
        [DataField]
        public TileRef? PreviousTileRef;

        /// <summary>
        /// Percentage of the reagent that is reacted with the TileReaction.
        /// <example>
        /// 0.5 = 50% of the reagent is reacted.
        /// </example>
        /// </summary>
        [DataField]
        public float TransferAmountPercentage;

        /// <summary>
        /// The minimum amount of the reagent that will be reacted with the TileReaction.
        /// We do this to prevent floating point issues. A reagent with a low percentage transfer amount will
        /// transfer 0.01~ forever and never get deleted.
        /// <remarks>Defaults to 0.05 if not defined, a good general value.</remarks>
        /// </summary>
        [DataField]
        public float MinimumTransferAmount = 0.05f;

        [DataField]
        public bool Active;

        // KS14 start: a client's kept copy can hit a wall while a state is applied, where it must not react yet
        /// <summary>
        ///     Whether the vapor has hit a wall and stops at its next update. See <c>SharedVaporSystem.CanStopNow</c>.
        /// </summary>
        [ViewVariables]
        public bool KsStopPending;
        // KS14 end
    }
}
