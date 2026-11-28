using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation;

/// <summary>
///     Which techniques the path of qi opens at each realm, shared so the client can show what's coming.
/// </summary>
public static class CultivationTechniqueLists
{
    /// <summary>
    ///     Techniques every cultivator comes to, by the realm that opens them.
    /// </summary>
    public static readonly (EntProtoId Action, CultivationRealm Realm)[] Universal =
    {
        ("ActionCultivationSpiritualSense", CultivationRealm.QiCondensation),
        ("ActionJianghuDuel", CultivationRealm.QiCondensation),
        ("ActionCultivationEmptyPalm", CultivationRealm.QiCondensation),
        ("ActionCultivationQinggong", CultivationRealm.QiCondensation),
        ("ActionCultivationWriteTalisman", CultivationRealm.QiCondensation),
        ("ActionCultivationRealmPressure", CultivationRealm.FoundationEstablishment),
        ("ActionJianghuFoundSect", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationAcupointSealing", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationConcealment", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationForcedCirculation", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationForgetLaw", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationArrays", CultivationRealm.FoundationEstablishment),
        ("ActionCultivationVoidStep", CultivationRealm.GoldenCore),
        ("ActionCultivationBodyOutsideBody", CultivationRealm.GoldenCore),
    };

    /// <summary>
    ///     Left out in Wuxia: Ascension, where hiding one's cultivation would only stall the race to the peak.
    /// </summary>
    public static readonly EntProtoId Concealment = "ActionCultivationConcealment";

    /// <summary>
    ///     The body arts each stage opens, the forge from the start.
    /// </summary>
    public static readonly (EntProtoId Action, int Stage)[] BodyStageArts =
    {
        ("ActionBodyForge", 0),
        ("ActionJianghuDuel", 0),
        ("ActionBodyBreathOfRenewal", 1),
        ("ActionBodyIronShirt", 2),
        ("ActionBodyMountainLeap", 2),
        ("ActionBodyEarthStomp", 2),
        ("ActionBodyShatteringFist", 3),
        ("ActionBodyHundredFists", 3),
        ("ActionBodyBoneSetting", 3),
        ("ActionBodyStance", 3),
        ("ActionBodyDisciple", 3),
        ("ActionBodyRemold", 4),
        ("ActionBodyBullCharge", 4),
        ("ActionBodyBloodBoil", 5),
        ("ActionBodyFallingMountain", 5),
        ("ActionBodyMountainHurl", 5),
        ("ActionBodySkySplittingPalm", 6),
        ("ActionBodyVajra", 7),
        ("ActionBodyHeavenQuake", 8),
        ("ActionBodyPrimordialRoar", 9),
    };
}
