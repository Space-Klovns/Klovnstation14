namespace Content.Server._KS14.Entry;

public static class KsIgnoredComponents
{
    public static string[] List => [
        "DirectionalSpriteManipulation",
        "KsWaveDistortion",
        "KsShadow",
        "KsRcdPlacementNoHint",
        "KsAlwaysDisplaced",
        "SupplyPodDrawDepth"
        // KsSpriteFadeOut and KsTrailFade are shared, networked and EnsureComp'd by server systems,
        //     so the server registers them for real - ignoring them here throws on startup.
    ];
}
