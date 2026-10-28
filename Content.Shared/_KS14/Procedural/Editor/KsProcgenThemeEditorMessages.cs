using System.Linq;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Procedural.Editor;

/// <summary>Editable room style, independent of shape and fixture coordinates.</summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class KsProcgenThemeEditorValues
{
    [DataField] public string Name = "Room theme";
    [DataField] public string TilePack = string.Empty;
    [DataField] public string WallPack = string.Empty;
    [DataField] public string LightPack = string.Empty;
    [DataField] public string EntityPack = string.Empty;
    [DataField] public string AtmosphereProfile = string.Empty;
    [DataField] public string ElectricalProfile = string.Empty;
    [DataField] public float FurnishingDensity = 0.2f;
    [DataField] public float LightingCoverage = 0.85f;
    [DataField] public bool CoherentContents = true;
    [DataField] public bool KeepCenterOpen = true;
    [DataField] public bool AllowLooseItems = true;

    public KsProcgenThemeEditorValues Copy() => new()
    {
        Name = Name, TilePack = TilePack, WallPack = WallPack, LightPack = LightPack, EntityPack = EntityPack,
        AtmosphereProfile = AtmosphereProfile, ElectricalProfile = ElectricalProfile,
        FurnishingDensity = FurnishingDensity, LightingCoverage = LightingCoverage,
        CoherentContents = CoherentContents, KeepCenterOpen = KeepCenterOpen, AllowLooseItems = AllowLooseItems,
    };

    public bool TryBuild(Guid documentId, int revision, out KsProcgenContentDraft? draft, out KsProcgenIssue? issue)
    {
        draft = null;
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 128 ||
            new[] { TilePack, WallPack, LightPack, EntityPack, AtmosphereProfile, ElectricalProfile }.Any(value => value == null || value.Length > 128) ||
            !float.IsFinite(FurnishingDensity) || FurnishingDensity is < 0 or > 1 ||
            !float.IsFinite(LightingCoverage) || LightingCoverage is < 0 or > 1)
        { issue = new("InvalidThemeEditorValues", "A bounded name, pack IDs and fractions between zero and one are required."); return false; }
        List<KsProcgenWeightedPackReference> Pack(string id) => string.IsNullOrEmpty(id) ? new() : new() { new() { Pack = id } };
        return KsProcgenContentDraft.TrySnapshot(new()
        {
            DocumentId = documentId.ToString("N"), Revision = revision,
            Themes = new() { ["Theme"] = new()
            {
                AtmosphereProfile = string.IsNullOrEmpty(AtmosphereProfile) ? null : AtmosphereProfile,
                ElectricalProfile = string.IsNullOrEmpty(ElectricalProfile) ? null : ElectricalProfile,
                TilePacks = Pack(TilePack), WallPacks = Pack(WallPack), LightingPacks = Pack(LightPack),
                EntityPacks = Pack(EntityPack), Goals = new()
                {
                    FurnishingDensity = FurnishingDensity, LightingCoverage = LightingCoverage,
                    CoherentContents = CoherentContents, KeepCenterOpen = KeepCenterOpen, AllowLooseItems = AllowLooseItems,
                },
            } },
        }, out draft, out issue);
    }
}

[Serializable, NetSerializable]
public sealed class KsProcgenThemeEditorState : EuiStateBase
{
    public Guid DocumentId;
    public int Revision;
    public KsProcgenThemeEditorValues Values = new();
    public string[] TilePacks = [];
    public string[] WallPacks = [];
    public string[] LightPacks = [];
    public string[] EntityPacks = [];
    public string[] AtmosphereProfiles = [];
    public string[] ElectricalProfiles = [];
    public string Status = string.Empty;
    public string Detail = string.Empty;
    public bool CatalogTruncated;
}

[Serializable, NetSerializable]
public sealed class KsProcgenThemeEditorSave : EuiMessageBase
{
    public int ExpectedRevision;
    public KsProcgenThemeEditorValues Values = new();
}
