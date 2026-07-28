namespace Content.Server.NPC.Queries.Curves;

public sealed partial class BoolCurve : IUtilityCurve
{
    // KS14: ANK: Start
    [DataField] public float LowScore = 0f;
    [DataField] public float HighScore = 1f;
    // KS14: ANK: End
}
