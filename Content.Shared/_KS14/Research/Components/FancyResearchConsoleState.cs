using Robust.Shared.Serialization;
using Content.Shared._KS14.Research;

namespace Content.Shared._KS14.Research.Components
{
    [Serializable, NetSerializable]
    public sealed class FancyResearchConsoleState : BoundUserInterfaceState
    {
        public int Points;

        /// <summary>
        /// Goobstation field - all researches and their availablities
        /// </summary>
        public Dictionary<string, ResearchAvailability> Researches;

        public FancyResearchConsoleState(int points, Dictionary<string, ResearchAvailability> researches) // Goobstation R&D console rework = researches field
        {
            Points = points;
            Researches = researches; // Goobstation R&D console rework
        }
    }
}
