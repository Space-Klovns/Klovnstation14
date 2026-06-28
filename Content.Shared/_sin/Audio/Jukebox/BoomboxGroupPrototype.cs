using Robust.Shared.Prototypes;

namespace Content.Shared._sin.Audio.Jukebox;

// _sin start
/// <summary>
/// A search group for the boombox/jukebox UI. Tracks whose
/// SearchTag list contains any matching GroupSearchTag will be
/// shown when this group is selected.
/// </summary>
[Prototype("_sin_boombox_group")]
public sealed partial class BoomboxGroupPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly name shown on the group button.
    /// </summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>
    /// Tags this group searches for inside each track's SearchTag list.
    /// </summary>
    [DataField]
    public List<string> GroupSearchTag = new();

    /// <summary>
    /// Search tags used to find this group in the Groups tab search bar.
    /// </summary>
    [DataField]
    public List<string> GroupTag = new();
}
// _sin end
